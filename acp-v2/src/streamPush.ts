import { createServer, type IncomingMessage, type ServerResponse, type Server } from "node:http";
import { Buffer } from "node:buffer";
import type { AcpAgent } from "@virtuals-protocol/acp-node-v2";

// Internal HTTP server for the inJobStream PushMode. The C# tier
// (InJobStreamDeliveryService) POSTs each tick payload here, and this server
// translates the call into an SDK message on the kept-open ACP job:
//
//   POST /v1/internal/push-tick     → agent.sendMessage(chainId, jobId, payloadJson, "structured")
//   POST /v1/internal/submit-final  → session.submit(finalPayloadJson)  (closes job)
//   GET  /health                    → liveness
//
// Auth: X-API-Key required on the two POST endpoints; matches the bot's
// BASICSUBSCRIPTIONBOT_API_KEY (same secret as the C# tier middleware).
//
// Bind only on the docker-internal bridge — Caddy MUST NOT forward this port.
// Default port 6001 matches InJobStreamDeliveryService's default BaseUrl.
//
// We deliberately use the SDK's REST send path (agent.sendMessage, awaitable +
// durable) rather than the transport-push agent.sendJobMessage (fire-and-
// forget). Trades ~250ms latency for delivery confidence — the right choice
// for the Phase-1 60s-cadence smoke. Sub-second streams in Phase 2+ can
// switch to sendJobMessage per-offering.

const MAX_BODY_BYTES = 1_048_576; // 1 MB matches C# InJobStreamDeliveryService cap

export interface StreamPushServerOptions {
  agent: AcpAgent;
  apiKey?: string;
  port: number;
}

interface PushTickBody {
  subscriptionId?: string;
  chainId?: number;
  jobId?: string;
  tickNumber?: number;
  payloadJson?: string;
}

interface SubmitFinalBody {
  subscriptionId?: string;
  chainId?: number;
  jobId?: string;
  finalPayloadJson?: string;
}

export function startStreamPushServer(opts: StreamPushServerOptions): Server {
  const { agent, apiKey, port } = opts;

  const server = createServer(async (req, res) => {
    try {
      await handle(req, res, agent, apiKey);
    } catch (err) {
      console.error("[streamPush] unhandled error:", err);
      writeJson(res, 500, { error: "internal" });
    }
  });

  server.listen(port, () => {
    console.log(`[streamPush] listening on :${port}`);
  });
  return server;
}

async function handle(
  req: IncomingMessage,
  res: ServerResponse,
  agent: AcpAgent,
  apiKey: string | undefined
) {
  const url = req.url ?? "/";
  const method = req.method ?? "GET";

  if (method === "GET" && url === "/health") {
    writeJson(res, 200, { status: "ok", time: new Date().toISOString() });
    return;
  }

  if (method !== "POST") { writeJson(res, 405, { error: "method not allowed" }); return; }

  // Auth — same secret as the C# tier; constant-time compare.
  if (apiKey) {
    const provided = req.headers["x-api-key"];
    const providedStr = Array.isArray(provided) ? provided[0] : provided;
    if (!providedStr || !timingSafeEqual(providedStr, apiKey)) {
      writeJson(res, 401, { error: "unauthorized" });
      return;
    }
  }

  if (url === "/v1/internal/push-tick") {
    const body = await readJsonBody<PushTickBody>(req);
    if (!body) { writeJson(res, 400, { error: "invalid body" }); return; }
    const err = validatePushTick(body);
    if (err) { writeJson(res, 400, { error: err }); return; }
    try {
      // sendMessage = REST fallback (awaitable, durable). Keeps the job open
      // (no submit). Returns void on success; throws on transport/auth/etc.
      await agent.sendMessage(body.chainId!, body.jobId!, body.payloadJson!, "structured");
      writeJson(res, 200, { ok: true, subscriptionId: body.subscriptionId, tickNumber: body.tickNumber });
    } catch (sdkErr) {
      const message = sdkErr instanceof Error ? sdkErr.message : String(sdkErr);
      console.warn(`[streamPush] sendMessage failed for sub=${body.subscriptionId} job=${body.jobId}: ${message}`);
      // 502 = upstream (transport / SDK) failure — RetryWorker should retry.
      writeJson(res, 502, { error: "sendMessage failed", detail: message });
    }
    return;
  }

  if (url === "/v1/internal/submit-final") {
    const body = await readJsonBody<SubmitFinalBody>(req);
    if (!body) { writeJson(res, 400, { error: "invalid body" }); return; }
    const err = validateSubmitFinal(body);
    if (err) { writeJson(res, 400, { error: err }); return; }
    const session = agent.getSession(body.chainId!, body.jobId!);
    if (!session) {
      // 410 Gone = session is no longer in the agent's active set (job
      // expired, completed elsewhere, sidecar restarted before hydrate).
      // C# tier treats as terminal for this row.
      writeJson(res, 410, { error: "session not active", subscriptionId: body.subscriptionId });
      return;
    }
    try {
      await session.submit(body.finalPayloadJson!);
      writeJson(res, 200, { ok: true, subscriptionId: body.subscriptionId });
    } catch (sdkErr) {
      const message = sdkErr instanceof Error ? sdkErr.message : String(sdkErr);
      console.warn(`[streamPush] submit failed for sub=${body.subscriptionId} job=${body.jobId}: ${message}`);
      writeJson(res, 502, { error: "submit failed", detail: message });
    }
    return;
  }

  writeJson(res, 404, { error: "not found" });
}

function validatePushTick(b: PushTickBody): string | null {
  if (!b.subscriptionId || typeof b.subscriptionId !== "string") return "subscriptionId required";
  if (typeof b.chainId !== "number" || b.chainId <= 0)            return "chainId required (positive number)";
  if (!b.jobId || typeof b.jobId !== "string")                    return "jobId required";
  if (typeof b.tickNumber !== "number" || b.tickNumber < 1)       return "tickNumber required (>=1)";
  if (typeof b.payloadJson !== "string" || b.payloadJson.length === 0) return "payloadJson required";
  return null;
}

function validateSubmitFinal(b: SubmitFinalBody): string | null {
  if (!b.subscriptionId || typeof b.subscriptionId !== "string") return "subscriptionId required";
  if (typeof b.chainId !== "number" || b.chainId <= 0)            return "chainId required (positive number)";
  if (!b.jobId || typeof b.jobId !== "string")                    return "jobId required";
  if (typeof b.finalPayloadJson !== "string" || b.finalPayloadJson.length === 0)
    return "finalPayloadJson required";
  return null;
}

async function readJsonBody<T>(req: IncomingMessage): Promise<T | null> {
  const chunks: Buffer[] = [];
  let total = 0;
  for await (const chunk of req) {
    const buf = chunk as Buffer;
    total += buf.length;
    if (total > MAX_BODY_BYTES) return null;
    chunks.push(buf);
  }
  const raw = Buffer.concat(chunks).toString("utf8");
  try { return JSON.parse(raw) as T; } catch { return null; }
}

function writeJson(res: ServerResponse, status: number, body: unknown) {
  const json = JSON.stringify(body);
  res.writeHead(status, {
    "content-type": "application/json",
    "content-length": Buffer.byteLength(json),
  });
  res.end(json);
}

function timingSafeEqual(a: string, b: string): boolean {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}
