# BasicSubscriptionBot — ACP 2.0 Subscription Boilerplate

Sibling to ACP_BasicBot. Same two-tier shape (TS sidecar + .NET 10 API + SQLite), plus a worker loop and webhook push delivery for **subscription / recurring offerings**. Clone this folder, rename, and replace the stub `tick_echo` with your real subscription logic.

## When to use this vs ACP_BasicBot

| Bot needs | Start from |
|---|---|
| Only one-shot offerings (request → reply, done) | ACP_BasicBot |
| Any subscription / "watch X" / scheduled push offering | ACP_BasicSubscriptionBot |
| Both shapes in one bot | ACP_BasicSubscriptionBot (handles both) |

## Architecture

```
acp-v2/   (Node 22 / TypeScript)            BasicSubscriptionBot.Api/   (.NET 10)
@virtuals-protocol/acp-node-v2  ──HTTP──►  ADO.NET + SQLite + 2 hosted workers
                                            (TickScheduler + Retry)
                                            +
                                            HTTPS POST + HMAC-SHA256
                                              ─────────────────►  Buyer's webhook
```

## How a subscription works

1. Buyer hires a subscription offering (e.g. `tick_echo`) with `{ ticks: 24, intervalSeconds: 3600, webhookUrl, ... }`.
2. Sidecar validates, computes price `pricePerTickUsdc × ticks`, calls `setBudget`.
3. Buyer funds. Sidecar calls `POST /subscriptions` on the C# API.
4. C# inserts a row, generates a 32-byte HMAC secret, returns it.
5. Sidecar `submit()`s a **subscription receipt** containing `subscriptionId` + `webhookSecret`. ACP job done.
6. `TickSchedulerWorker` fires on schedule, computes the tick payload, POSTs to the buyer's webhook with HMAC headers.
7. After N ticks: subscription marked `completed`. Done.

## Local development

Two terminals:

```bash
# Terminal 1 — C# API on http://localhost:5000
cd BasicSubscriptionBot.Api
dotnet run
```

```bash
# Terminal 2 — ACP sidecar
cd acp-v2
cp .env.example .env       # then fill in agent credentials
npm install
npm run dev
```

For local subscription testing without HTTPS:
```powershell
$env:ALLOW_INSECURE_WEBHOOKS = "true"
```

## Smoke tests

(Same 7 acceptance tests as in `docs/superpowers/specs/2026-05-03-acp-basicsubscriptionbot-boilerplate-design.md`.)

## Cloning for a new bot

1. Copy `ACP_BasicSubscriptionBot/` → `ACP_MyNewBot/`
2. Find/replace `BasicSubscriptionBot` → `MyNewBot` (case-sensitive) in: folder, .sln, .csproj, namespaces, package.json `name`, docker-compose service/container names, env var names (`BASICSUBSCRIPTIONBOT_*` → `MYNEWBOT_*`).
3. Provision a new agent on app.virtuals.io, copy creds into `acp-v2/.env`.
4. Replace stub offerings:
   - Delete `tick_echo.ts` (or keep + add your own).
   - Delete `tick_echo_state` table + `TickEchoRepository` if not needed.
   - Add your real subscription offerings to `src/offerings/`, register in `registry.ts`. Every `Offering` carries `slaMinutes` (min 5), `requirementSchema`, `requirementExample`, `deliverableSchema`, and `deliverableExample` — fill all of them from the C# response model (camelCase keys via ASP.NET Core's web defaults). Subscription offerings ALSO declare a `subscription.tiers` list of `{name, priceUsd, durationDays}` (duration in {7, 15, 30, 90} days) which becomes the marketplace registration tier list. For subscription offerings the deliverable shape is the **subscription receipt** returned at hire time, not the per-tick webhook payload.
   - Update `TickExecutorService.cs` to route by your offering names.
   - Update `pricing.ts` and validators if your subscription has different bounds.
5. If you don't need the one-shot path: delete `echo.ts`, `EchoRepository.cs`, `EchoService.cs`, `EchoRecord.cs`, `echo_records` table, `/echo` endpoints.
6. `npm run print-offerings` and register on app.virtuals.io.

## What's intentionally NOT in this shell

- Cancellation / refunds (subscription runs to completion)
- EAS attestation per tick (left as `// TODO:` — opt in per bot via the `acp-shared` network into ACP_EASIssuer)
- Pull-fallback delivery (webhook only)
- Subscription renewal (buyer hires again)
- Multi-replica / leader election (single replica per bot)

## Security

Webhook scheme: HTTPS only (override `ALLOW_INSECURE_WEBHOOKS=true` for dev). HMAC-SHA256 signature header `X-Subscription-Signature`. Webhook secret returned **once** in the receipt deliverable — buyer must persist. Buyers MUST treat duplicate `(subscriptionId, tick)` as no-op (we may retry).

See `docs/superpowers/specs/2026-05-03-acp-basicsubscriptionbot-boilerplate-design.md` for full design.
