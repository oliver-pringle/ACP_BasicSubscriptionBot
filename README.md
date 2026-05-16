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

## Security defaults (do not deviate without explicit reason)

- **`BASICSUBSCRIPTIONBOT_API_KEY` is required in any non-Development environment.** The boot will throw `InvalidOperationException` if `ASPNETCORE_ENVIRONMENT != "Development"` and the env var is unset, so a misconfigured droplet deploy can't silently start in fail-open mode. In Development the bot still boots without it (with a loud warning) so local clones work out-of-the-box.
- **`webhookUrl` is SSRF-validated on subscribe + on every delivery tick.** `Services/WebhookUrlValidator.cs` rejects loopback, RFC1918, link-local (incl. the AWS/GCP/Azure metadata IP `169.254.169.254`), IPv6 ULA/link-local, carrier-grade NAT, and any non-`https://` scheme. DNS is resolved at validate-time and every resolved address is checked. Set `ALLOW_INSECURE_WEBHOOKS=true` to bypass — dev/test only.
- **Subscription inputs are bounded** in `SubscriptionService.CreateAsync`: `intervalSeconds` 60..86400, `ticks` 1..10000, total window ≤90 days, `requirementJson` ≤16 KB. Bump constants per-bot only if a specific offering needs different shape.
- **`GET /subscriptions/{id}` returns `SubscriptionView`, not `Subscription`.** The full record holds the HMAC `WebhookSecret` used by buyers to verify tick deliveries — never echo it over an unauthenticated route, or anyone with the subscriptionId can forge ticks.

## Smoke tests

(Same 7 acceptance tests as in `docs/superpowers/specs/2026-05-03-acp-basicsubscriptionbot-boilerplate-design.md`.)

## Wallet delegation guard (EIP-7702)

The sidecar runs a boot-time delegation check before accepting any hires. The
ACP v2 SDK (`acp-node-v2 ^0.0.6`) only recognises wallets delegated to Alchemy
ModularAccountV2 (`0x69007702764179f14F51cdce752f4f775d74E139`). Privy WaaS
occasionally rotates a wallet to a different impl; when that happens, the next
hire fails inside the SDK with `Expected bigint, got: N` from a HexBigInt
typebox encoder that's been fed the wallet's raw integer nonce.

`acp-v2/src/walletDelegation.ts` makes the sidecar self-defending against this:

- **On every boot:** one `eth_getBytecode` call probes the wallet. If the
  delegation prefix (`0xef0100…`) points at ModularAccountV2, the sidecar
  carries on. If not, it either auto-recovers or refuses to start.
- **Auto-recovery (recommended):** set `DEPLOYER_PRIVATE_KEY` in
  `acp-v2/.env`. The guard signs a fresh 7702 authorization via Privy's
  `signer.signAuthorization` and broadcasts a sponsored type-4 tx from the
  deployer EOA. The deployer pays gas (~0.001 ETH per recovery, rare in
  practice). No on-chain tx when delegation is already correct — idempotent.
- **Without a deployer key:** the guard throws on drift with a recovery
  message pointing at `scripts/provision-7702.ts` for a manual one-shot.

`BASE_RPC_URL` in `acp-v2/.env` overrides the public RPC the probe uses
(defaults to publicnode). Even a free RPC is fine — one call per boot.

The guard is wired into `seller.ts` right after `AcpAgent.create(...)`. Do
not remove it. The pattern is shared with ChainlinkBot, where it was
battle-tested through the 2026-05-11 Base mainnet cutover. Especially
important for subscription bots — a wallet drift between subscription
hires would silently break a multi-tick subscription mid-run.

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
5. If you don't need the one-shot path: delete `echo.ts`, `EchoRepository.cs`, `EchoService.cs`, `EchoRecord.cs`, `echo_records` table, `/echo` endpoints, and the `/v1/resources/echoStatus` route + `echoStatus` entry in `src/resources.ts`.
6. Replace the TS resources (optional — delete the example if your bot won't expose any):
   - `acp-v2/src/resources.ts` → your real resources. Resources are public, free, parameterised endpoints buyer / orchestrator agents (Butler etc.) call BEFORE paying for an offering. The example `echoStatus` shows the pattern: declare metadata here, wire the matching `/v1/resources/<name>` handler in `Program.cs`.
   - The X-API-Key middleware in `Program.cs` already whitelists `/v1/resources/*` so resources stay reachable when auth is on.
7. `npm run print-offerings` and register on app.virtuals.io. If you have resources, also run `npm run print-resources` and paste each block into the dashboard's Resources tab.

## What's intentionally NOT in this shell

- Cancellation / refunds (subscription runs to completion)
- EAS attestation per tick (left as `// TODO:` — opt in per bot via the `acp-shared` network into ACP_EASIssuer)
- Pull-fallback delivery (webhook only)
- Subscription renewal (buyer hires again)
- Multi-replica / leader election (single replica per bot)

## Security

Webhook scheme: HTTPS only (override `ALLOW_INSECURE_WEBHOOKS=true` for dev). HMAC-SHA256 signature header `X-Subscription-Signature`. Webhook secret returned **once** in the receipt deliverable — buyer must persist. Buyers MUST treat duplicate `(subscriptionId, tick)` as no-op (we may retry).

See `docs/superpowers/specs/2026-05-03-acp-basicsubscriptionbot-boilerplate-design.md` for full design.
