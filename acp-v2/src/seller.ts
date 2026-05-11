import { AcpAgent } from "@virtuals-protocol/acp-node-v2";
import type { JobSession, JobRoomEntry } from "@virtuals-protocol/acp-node-v2";
import { loadEnv } from "./env.js";
import { createProvider } from "./provider.js";
import { createApiClient } from "./apiClient.js";
import { route } from "./router.js";
import { priceForAssetToken } from "./pricing.js";
import { toDeliverable } from "./deliverable.js";
import { listOfferings, getOffering } from "./offerings/registry.js";
import { listResources } from "./resources.js";
import { ensureDelegation } from "./walletDelegation.js";
import { getChain } from "./chain.js";

type PendingJob = {
  offeringName: string;
  requirement: Record<string, unknown>;
};

async function main() {
  const env = loadEnv();
  const client = createApiClient(env.apiUrl, { apiKey: env.apiKey });

  console.log(`[seller] chain=${env.chain} wallet=${env.walletAddress}`);
  console.log(`[seller] api=${env.apiUrl}`);
  console.log(`[seller] offerings registered (in code): ${listOfferings().length}`);
  console.log(`[seller] resources registered (in code): ${listResources().length}`);

  const provider = await createProvider(env);
  const agent = await AcpAgent.create({ provider });

  // Guard against EIP-7702 delegation drift. The ACP v2 SDK only recognises
  // wallets delegated to Alchemy ModularAccountV2; any other delegation
  // causes the next hire to fail with `Expected bigint, got: N`. Empirically
  // the drift triggers between PrivyAlchemyEvmProviderAdapter.create() and
  // the first hire, so re-check AFTER agent setup and auto-recover if a
  // DEPLOYER_PRIVATE_KEY sponsor is configured. See acp-v2/src/walletDelegation.ts
  // and user-memory reference_acp_wallet_provisioning.md.
  await ensureDelegation({
    adapter: provider,
    walletAddress: env.walletAddress,
    chain: getChain(env.chain),
    rpcUrl: env.baseRpcUrl,
    deployerPrivateKey: env.deployerPrivateKey,
  });

  const pending = new Map<string, PendingJob>();

  agent.on("entry", async (session: JobSession, entry: JobRoomEntry) => {
    try {
      if (entry.kind === "system") {
        switch (entry.event.type) {
          case "job.created":   console.log(`[seller] job.created jobId=${session.jobId}`); return;
          case "job.funded":    return await handleJobFunded(session);
          case "job.completed": pending.delete(session.jobId); console.log(`[seller] job.completed jobId=${session.jobId}`); return;
          case "job.rejected":  pending.delete(session.jobId); console.log(`[seller] job.rejected jobId=${session.jobId}`); return;
          case "job.expired":   pending.delete(session.jobId); return;
          default: return;
        }
      }
      if (entry.kind === "message" && entry.contentType === "requirement") {
        await handleRequirement(session, entry);
      }
    } catch (err) {
      console.error(`[seller] handler error for job ${session.jobId}:`, err);
    }
  });

  async function handleRequirement(session: JobSession, entry: JobRoomEntry) {
    if (entry.kind !== "message") return;

    let requirement: Record<string, unknown>;
    try { requirement = JSON.parse(entry.content); }
    catch { await session.sendMessage("invalid requirement payload"); return; }

    const job = session.job ?? (await session.fetchJob());
    const offeringName = job.description;

    const ZERO_ADDRESS = "0x0000000000000000000000000000000000000000";
    if (job.evaluatorAddress.toLowerCase() !== ZERO_ADDRESS) {
      await session.sendMessage(
        `unsupported: this seller only accepts jobs with evaluatorAddress=${ZERO_ADDRESS}. Got: ${job.evaluatorAddress}`
      );
      return;
    }

    const offering = getOffering(offeringName);
    if (!offering) { await session.sendMessage(`unknown offering: ${offeringName}`); return; }

    const v = offering.validate(requirement);
    if (!v.valid) { await session.sendMessage(v.reason ?? "validation failed"); return; }

    const price = await priceForAssetToken(offeringName, requirement, session.chainId);
    await session.setBudget(price);

    pending.set(session.jobId, { offeringName, requirement });
  }

  async function handleJobFunded(session: JobSession) {
    const stash = pending.get(session.jobId);
    if (!stash) {
      console.warn(`[seller] job.funded without stashed requirement, jobId=${session.jobId}`);
      return;
    }
    const offering = getOffering(stash.offeringName);
    if (!offering) {
      await session.sendMessage(`unknown offering at funded time: ${stash.offeringName}`);
      return;
    }

    if (offering.subscription) {
      // Subscription path: create subscription, submit receipt
      const buyerAgent = (session.job?.clientAddress ?? "0x").toString();
      let receipt;
      try {
        receipt = await client.createSubscription({
          jobId: session.jobId,
          buyerAgent,
          offeringName: stash.offeringName,
          requirement: stash.requirement
        });
      } catch (err) {
        await session.sendMessage(`subscription creation failed: ${(err as Error).message}`);
        return;
      }
      const payload = await toDeliverable(session.jobId, {
        subscriptionId: receipt.subscriptionId,
        webhookSecret: receipt.webhookSecret,
        ticksPurchased: receipt.ticksPurchased,
        intervalSeconds: receipt.intervalSeconds,
        expiresAt: receipt.expiresAt,
        signatureScheme: "HMAC-SHA256(secret, tick + '.' + timestamp + '.' + body)"
      });
      await session.submit(payload);
      console.log(`[seller] submitted subscription receipt jobId=${session.jobId} subId=${receipt.subscriptionId}`);
      return;
    }

    // One-shot path
    const outcome = await route(stash.offeringName, stash.requirement, { client });
    if (!outcome.ok) { await session.sendMessage(`execution failed: ${outcome.reason}`); return; }
    const payload = await toDeliverable(session.jobId, outcome.result);
    await session.submit(payload);
    console.log(`[seller] submitted one-shot jobId=${session.jobId} offering=${stash.offeringName}`);
  }

  await agent.start();

  const shutdown = async (signal: string) => {
    console.log(`[seller] ${signal} received, stopping agent`);
    try { await agent.stop(); } finally { process.exit(0); }
  };
  process.on("SIGINT", () => void shutdown("SIGINT"));
  process.on("SIGTERM", () => void shutdown("SIGTERM"));

  console.log("[seller] running — waiting for jobs");
}

main().catch((err) => { console.error("[seller] fatal:", err); process.exit(1); });
