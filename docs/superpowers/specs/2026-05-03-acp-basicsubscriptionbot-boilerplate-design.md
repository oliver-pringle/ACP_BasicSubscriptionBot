# ACP BasicSubscriptionBot Boilerplate — Design

**Date:** 2026-05-03
**Author:** Oliver Pringle (with Claude)
**Status:** Approved (design accepted, implementing)
**Target directory:** `C:\code_crypto\ACP\ACP_BasicSubscriptionBot\` (flat, matches newer bots — `ACP_LiquidGuard\`, `ACP_MEVProtect\`, `ACP_EASIssuer\`)
**Sibling to:** `ACP_BasicBot\BasicBot\` (the one-shot boilerplate)

## Purpose

Build the second ACP 2.0 boilerplate: a sibling to BasicBot specialised for subscription / recurring offerings via worker-loop + webhook push. Many Tier-A/B bot ideas have a "watch X for me" tier (`autoperiodic` scans, `watchprice` limit orders, `alert-pro` health monitors, `watchroute` bridge alerts, `watchcollection` NFT alerts) — every one of those re-invents the same scheduler + push + retry plumbing. Building it once as boilerplate compounds: every future subscription-capable bot becomes faster to ship.

**Sibling, not superset.** New bots pick one starting point: `BasicBot` for one-shot offerings only, `BasicSubscriptionBot` for subscription offerings (and bots that need both shapes will start from `BasicSubscriptionBot`, which supports one-shot too).

**Not a marketplace product itself.** Like BasicBot, BasicSubscriptionBot is never deployed to production — it ships a trivial stub offering that proves the pattern end-to-end, then gets cloned-and-renamed for real bots.

## Why this is needed

ACP V2's job model is strictly **one-shot**: `session.submit(payload)` ends the ACP session, after which the buyer cannot be reached through the ACP channel. Every V2 offering surveyed (2026-05-03) has `priceType: "fixed"` — no native subscription pricing.

Therefore "subscription" in V2 must be modelled as: **one upfront-paid ACP job buys N future deliveries via an out-of-band channel**. The out-of-band channel chosen for v1 is the buyer's webhook (HTTPS POST). All subsequent push delivery happens between the seller's worker loop and the buyer's webhook endpoint, signed with HMAC.

## Architecture

```
┌──────────────────────────┐  HTTP (private bridge)   ┌─────────────────────────────────────┐
│  acp-v2/   (Node 22)     │  ─────────────────────►  │  BasicSubscriptionBot.Api  (.NET 10)│
│  TS sidecar              │                          │  ASP.NET Minimal API                │
│  @virtuals-protocol/     │  ◄─────────────────────  │  ADO.NET + SQLite                   │
│   acp-node-v2 v0.0.6     │       JSON deliverables  │  /data/basicsubscriptionbot.db      │
└──────────────────────────┘                          │                                     │
        │                                             │  + IHostedService TickScheduler     │
        ▼                                             │  + IHostedService RetryWorker       │
   Base / Base Sepolia                                │  + WebhookDeliveryService           │
   (signs jobs as agent)                              └─────────────────────────────────────┘
                                                                       │
                                                                       │ HTTPS POST + HMAC
                                                                       ▼
                                                        Buyer's webhook URL
                                                        (supplied in subscription requirement)
```

**Same two-tier shape as BasicBot.** New: two background services on the C# side that own the post-funded subscription lifecycle. The TS sidecar's responsibility ends after `session.submit()` returns the subscription receipt deliverable.

**Why C# owns the worker loop, not the sidecar.** The C# tier already owns SQLite, owns the offering business logic, and is the natural home for hosted services. Pushing scheduling into the sidecar would require a second SQLite client and a second copy of subscription state — pointless duplication.

## Folder Structure

```
C:\code_crypto\ACP\ACP_BasicSubscriptionBot\
├── BasicSubscriptionBot.sln
├── docker-compose.yml
├── README.md                                   # clone-and-rename checklist (mirrors BasicBot's)
├── .gitignore
├── data\
│   └── .gitkeep                                # SQLite file lives here (host bind-mount)
│
├── BasicSubscriptionBot.Api\
│   ├── BasicSubscriptionBot.Api.csproj
│   ├── Program.cs                              # registers Db, services, two hosted workers,
│   │                                           # one-shot endpoints, subscription endpoints
│   ├── appsettings.json
│   ├── Dockerfile
│   ├── Data\
│   │   ├── Db.cs                               # connection factory + schema bootstrap
│   │   ├── EchoRepository.cs                   # one-shot echo CRUD (BasicBot equivalent)
│   │   ├── SubscriptionRepository.cs           # subscription + run + suspension queries
│   │   └── TickEchoRepository.cs               # tick_echo subscription state
│   ├── Models\
│   │   ├── EchoRecord.cs
│   │   ├── Subscription.cs
│   │   ├── SubscriptionRun.cs
│   │   └── TickEchoState.cs
│   ├── Services\
│   │   ├── EchoService.cs                      # one-shot echo (BasicBot identical)
│   │   ├── SubscriptionService.cs              # create / lookup
│   │   ├── TickExecutorService.cs              # routes by offering name → produces payload
│   │   └── WebhookDeliveryService.cs           # HTTP POST + HMAC + retry calc
│   └── Workers\
│       ├── TickSchedulerWorker.cs              # IHostedService, scans due subs every 10s
│       └── RetryWorker.cs                      # IHostedService, retries failed deliveries every 30s
│
└── acp-v2\
    ├── package.json                            # name: basicsubscriptionbot-acp-v2
    ├── tsconfig.json
    ├── Dockerfile
    ├── .env.example
    ├── .gitignore
    ├── README.md
    ├── scripts\
    │   └── print-offerings-for-registration.ts
    └── src\
        ├── seller.ts                           # branches: one-shot vs subscription
        ├── env.ts
        ├── chain.ts
        ├── provider.ts
        ├── router.ts
        ├── pricing.ts                          # tick × pricePerTick math for subscriptions
        ├── deliverable.ts
        ├── apiClient.ts                        # POST /subscriptions, etc.
        ├── validators.ts                       # +webhook URL + interval + ticks validators
        └── offerings\
            ├── types.ts                        # Offering interface extended with subscription{}
            ├── registry.ts
            ├── echo.ts                         # one-shot stub (BasicBot identical)
            └── tick_echo.ts                    # subscription stub
```

## Subscription Model

**One ACP job buys one subscription with a fixed prepaid tick budget.** Buyer pays upfront, seller delivers N times via webhook, subscription auto-expires when ticks are exhausted OR a hard wall-clock cap is hit.

Buyer requirement (subscription offerings) MUST include three control fields plus offering-specific fields:

```json
{
  "webhookUrl": "https://buyer.example.com/acp/cb",
  "intervalSeconds": 3600,
  "ticks": 24,
  "<offering-specific fields>": "..."
}
```

Validation rules enforced by the sidecar BEFORE `session.setBudget()`:
- `webhookUrl` MUST be HTTPS (override via `ALLOW_INSECURE_WEBHOOKS=true` env, dev only)
- `intervalSeconds` MUST be ≥ `subscription.minIntervalSeconds` (default 60)
- `ticks` MUST be ≤ `subscription.maxTicks` (default 1000)
- `intervalSeconds × ticks` MUST be ≤ `subscription.maxDurationDays × 86400` (default 90 days)

Pricing: `priceForAssetToken` reads `pricePerTickUsdc` from the offering's `subscription` config and `ticks` from the requirement, computes `pricePerTickUsdc × ticks` in USDC, and calls `session.setBudget(AssetToken.usdc(total, chainId))`.

After `job.funded`, the sidecar:
1. Calls `POST /subscriptions` on the C# API with `{ jobId, buyerAgent, offeringName, requirement }`.
2. C# creates the subscription row, generates a 32-byte hex `webhookSecret`, returns `{ subscriptionId, webhookSecret, expiresAt }`.
3. Sidecar builds the **subscription receipt deliverable**:
   ```json
   {
     "subscriptionId": "<uuid>",
     "webhookSecret": "<32-byte-hex>",
     "ticksPurchased": 24,
     "intervalSeconds": 3600,
     "expiresAt": "2026-05-04T16:00:00Z",
     "signatureScheme": "HMAC-SHA256(secret, tick + '.' + timestamp + '.' + body)"
   }
   ```
4. `session.submit(receipt)`. ACP job done.

**The webhook secret is delivered ONCE in the receipt and is not retrievable later.** Buyer's agent must persist it. (Standard webhook industry pattern.)

## Push Delivery — Webhook

Each tick: C# `WebhookDeliveryService` issues `POST <subscription.webhookUrl>` with:

**Headers:**
- `Content-Type: application/json`
- `X-Subscription-Id: <subscriptionId>`
- `X-Subscription-Tick: <1..N>`
- `X-Subscription-Timestamp: <unix-seconds>`
- `X-Subscription-Signature: sha256=<hex>` where signature = `HMAC-SHA256(webhookSecret, "<tick>.<timestamp>.<body>")`

**Body:** the offering-specific tick payload, JSON.

**Success:** any 2xx response within 10s timeout.

**Failure handling:** non-2xx, network error, or timeout → schedule retry per backoff schedule below. Persists to `subscription_runs.delivery_status = retrying`.

**Idempotency contract:** the buyer's webhook MUST treat duplicate `(subscriptionId, tick)` as a no-op. The seller may retry. Tick numbers are strictly monotonic per subscription.

## Retry & Dead-Letter

Per-run retry schedule on transient failure: **30s, 2m, 10m, 1h, 6h** (5 attempts total).

After all 5 attempts exhausted: that specific run is marked `dead`. Subscription continues ticking on schedule for remaining ticks (one bad delivery doesn't kill the subscription — the buyer's webhook may have transient outages).

**Suspension trigger:** the subscription is marked `suspended` (stops scheduling new ticks) when `consecutive_failures` reaches 3. The counter increments on every initial-delivery failure in TickScheduler and resets to 0 on any successful delivery (initial OR via RetryWorker). So suspension only fires when the webhook is *persistently* down — a transient failure that RetryWorker recovers from resets the counter and keeps the subscription healthy. Manual ops to resume by setting `status='active', consecutive_failures=0` in SQLite.

## Schema (SQLite)

```sql
-- One row per active subscription
CREATE TABLE IF NOT EXISTS subscriptions (
    id                   TEXT PRIMARY KEY,         -- uuid
    job_id               TEXT NOT NULL UNIQUE,     -- ACP jobId, audit trail
    buyer_agent          TEXT NOT NULL,            -- on-chain address of buyer
    offering_name        TEXT NOT NULL,            -- e.g. "tick_echo"
    requirement_json     TEXT NOT NULL,            -- full original requirement
    webhook_url          TEXT NOT NULL,
    webhook_secret       TEXT NOT NULL,            -- 32-byte hex, returned ONCE in receipt
    interval_seconds     INTEGER NOT NULL,
    ticks_purchased      INTEGER NOT NULL,
    ticks_delivered      INTEGER NOT NULL DEFAULT 0,
    created_at           TEXT NOT NULL,            -- ISO 8601
    expires_at           TEXT NOT NULL,            -- min(start + ticks*interval, hard_cap)
    last_run_at          TEXT,
    next_run_at          TEXT NOT NULL,            -- denormalised for scheduler hot path
    status               TEXT NOT NULL,            -- active | completed | suspended | expired
    consecutive_failures INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_subs_due ON subscriptions(status, next_run_at);

-- One row per delivery attempt cycle (one tick = one row, regardless of retries)
CREATE TABLE IF NOT EXISTS subscription_runs (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    subscription_id TEXT NOT NULL REFERENCES subscriptions(id),
    tick_number     INTEGER NOT NULL,
    scheduled_at    TEXT NOT NULL,
    payload_json    TEXT NOT NULL,                 -- exact body delivered (audit)
    delivery_status TEXT NOT NULL,                 -- pending | delivered | retrying | dead
    attempts        INTEGER NOT NULL DEFAULT 0,
    next_attempt_at TEXT,                          -- when retrying
    last_attempt_at TEXT,
    last_error      TEXT,                          -- truncated to 1KB
    UNIQUE(subscription_id, tick_number)
);
CREATE INDEX IF NOT EXISTS ix_runs_retry ON subscription_runs(delivery_status, next_attempt_at);

-- Per-offering tick state (e.g. tick_echo's counter), keyed by subscription
CREATE TABLE IF NOT EXISTS tick_echo_state (
    subscription_id TEXT PRIMARY KEY REFERENCES subscriptions(id),
    message         TEXT NOT NULL,
    created_at      TEXT NOT NULL
);

-- One-shot echo (kept identical to BasicBot, for the side-by-side stub)
CREATE TABLE IF NOT EXISTS echo_records (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    message     TEXT    NOT NULL,
    received_at TEXT    NOT NULL
);
```

## Stub Offerings

The boilerplate ships **two** stubs to prove both shapes work end-to-end:

### `echo` (one-shot, identical to BasicBot)

Demonstrates that BasicSubscriptionBot still handles vanilla one-shot offerings. New bots that don't need this can delete it.

- Pricing: `0.10 USDC` flat
- Requirement: `{ "message": "<= 10000 chars>" }`
- Deliverable: `{ id, message, receivedAt }`

### `tick_echo` (subscription)

The "echo of subscription bots." Buyer says *"push me message X every Y seconds for N ticks."*

- Pricing: `0.01 USDC per tick` (so `ticks=24, intervalSeconds=3600 → $0.24` for 24 hourly pushes)
- `subscription` config: `{ pricePerTickUsdc: 0.01, minIntervalSeconds: 60, maxTicks: 1000, maxDurationDays: 90 }`
- Requirement schema:
  ```json
  {
    "message":         { "type": "string", "maxLength": 1000 },
    "webhookUrl":      { "type": "string", "format": "uri" },
    "intervalSeconds": { "type": "integer", "minimum": 60 },
    "ticks":           { "type": "integer", "minimum": 1, "maximum": 1000 }
  }
  ```
- Receipt deliverable (returned to buyer via ACP submit): subscription receipt as defined above.
- Per-tick webhook body:
  ```json
  {
    "subscriptionId": "<uuid>",
    "tick":           5,
    "totalTicks":     24,
    "message":        "<original message verbatim>",
    "deliveredAt":    "2026-05-03T16:00:00Z"
  }
  ```

New bots delete `tick_echo` and add real subscription offerings.

## Offering Interface (Extended)

```typescript
// acp-v2/src/offerings/types.ts
export interface Offering {
  name: string;
  description: string;
  requirementSchema: Record<string, unknown>;
  validate(req: Record<string, unknown>): ValidationResult;

  // Exactly one of the following two MUST be set.

  // One-shot path (identical to BasicBot)
  execute?(req: Record<string, unknown>, ctx: OfferingContext): Promise<unknown>;

  // Subscription path
  subscription?: {
    pricePerTickUsdc: number;
    minIntervalSeconds: number;     // default 60
    maxTicks: number;                // default 1000
    maxDurationDays: number;         // default 90
    // Tick payload computation lives on the C# side, looked up by name in
    // TickExecutorService. The TS side does NOT compute tick payloads — it
    // only handles the initial ACP job + receipt.
  };
}
```

`registry.ts`, `pricing.ts`, and `seller.ts` all branch on which is set.

## TickSchedulerWorker (C#)

```
loop forever:
    sleep(10s)
    rows = SELECT * FROM subscriptions
            WHERE status='active' AND next_run_at <= now()
            ORDER BY next_run_at LIMIT 100
    for each row in rows (concurrent up to 8):
        try:
            payload = TickExecutorService.compute(offering_name, requirement_json, ticks_delivered+1)
            INSERT subscription_runs (..., status='pending')
            ok, err = WebhookDeliveryService.deliver(row, payload, tick=ticks_delivered+1)
            if ok:
                UPDATE subscriptions SET ticks_delivered=ticks_delivered+1,
                                          last_run_at=now(),
                                          next_run_at=now()+interval_seconds,
                                          consecutive_failures=0,
                                          status = (CASE WHEN ticks_delivered+1 >= ticks_purchased THEN 'completed' ELSE 'active' END)
                UPDATE subscription_runs SET delivery_status='delivered'
            else:
                UPDATE subscriptions SET ticks_delivered=ticks_delivered+1,
                                          last_run_at=now(),
                                          next_run_at=now()+interval_seconds,
                                          consecutive_failures=consecutive_failures+1
                UPDATE subscription_runs SET delivery_status='retrying',
                                              attempts=1,
                                              next_attempt_at=now()+30s,
                                              last_error=err
                if consecutive_failures+1 >= 3:
                    UPDATE subscriptions SET status='suspended'
        catch fatal:
            log + continue (do not crash the worker)
```

**Tick advances even on failed delivery.** The buyer paid for N ticks regardless of whether their webhook was up — failed deliveries become retry candidates, not free re-tries.

## RetryWorker (C#)

```
loop forever:
    sleep(30s)
    rows = SELECT * FROM subscription_runs
            WHERE delivery_status='retrying' AND next_attempt_at <= now()
            ORDER BY next_attempt_at LIMIT 100
    for each run (concurrent up to 8):
        sub = SELECT * FROM subscriptions WHERE id = run.subscription_id
        if sub.status='suspended':
            continue   # don't waste retries on suspended subs
        ok, err = WebhookDeliveryService.deliver(sub, run.payload_json, tick=run.tick_number)
        if ok:
            UPDATE subscription_runs SET delivery_status='delivered', last_attempt_at=now()
            UPDATE subscriptions SET consecutive_failures=0
        else:
            attempts = run.attempts + 1
            if attempts >= 5:
                UPDATE subscription_runs SET delivery_status='dead', attempts=attempts, last_error=err, last_attempt_at=now()
            else:
                next = now() + backoff(attempts)   # 30s, 2m, 10m, 1h, 6h
                UPDATE subscription_runs SET attempts=attempts, next_attempt_at=next, last_error=err, last_attempt_at=now()
```

## Pricing Math — Concrete Example

Buyer hires `tick_echo` with:
```json
{ "message": "ping", "intervalSeconds": 3600, "ticks": 24, "webhookUrl": "https://buyer.example.com/cb" }
```

1. Sidecar validates: HTTPS ✓, 3600 ≥ 60 ✓, 24 ≤ 1000 ✓, `24 × 3600 = 86400 sec ≤ 90×86400` ✓
2. Sidecar computes price: `0.01 × 24 = 0.24 USDC`
3. `session.setBudget(AssetToken.usdc(0.24, chainId))`
4. Buyer funds. `job.funded` fires.
5. Sidecar `POST /subscriptions { jobId, buyerAgent, offeringName: "tick_echo", requirement: {...} }`
6. C# inserts row: `id=<uuid>`, `expires_at=now+86400s`, `next_run_at=now+3600s`, `webhook_secret=<32 random bytes hex>`, `status=active`. Returns `{ subscriptionId, webhookSecret, expiresAt }`.
7. Sidecar `session.submit({ subscriptionId, webhookSecret, ticksPurchased: 24, intervalSeconds: 3600, expiresAt, signatureScheme: "HMAC-SHA256(secret, tick + '.' + timestamp + '.' + body)" })`. ACP job complete.
8. TickSchedulerWorker fires hourly: computes `{ subscriptionId, tick: N, totalTicks: 24, message: "ping", deliveredAt: ... }`, POSTs to webhook with HMAC headers. Buyer verifies signature, processes payload.
9. After 24 ticks (~24h elapsed): `status` flips to `completed`. No more deliveries.

## Security Defaults

Inherits all of BasicBot's defaults plus:

| Concern | Default | Notes |
|---|---|---|
| Webhook scheme | HTTPS only | `ALLOW_INSECURE_WEBHOOKS=true` enables HTTP for local dev only |
| Webhook signature | HMAC-SHA256, secret returned ONCE in receipt | Industry standard. Buyers must persist secret. |
| Webhook timestamp skew | Strict — buyers should reject signatures whose timestamp is > 5 min from now | Boilerplate documents this in README |
| Internal `/subscriptions` API | Off-by-default `X-API-Key` like BasicBot | Same `BASICSUBSCRIPTIONBOT_API_KEY` env var pattern |
| Webhook URL hosts | Must NOT resolve to RFC1918 / loopback / link-local in prod | SSRF defence. Override with `ALLOW_INSECURE_WEBHOOKS=true` for dev. |
| Webhook outbound timeout | 10s connect + read | Prevents slow-loris by buyer |
| Webhook body cap | 1 MB | Tick payloads are small; oversize triggers a programming bug, not a feature |
| Container user | API runs as `app` (UID 1654) per BasicBot convention | Same `chown 1654:1654 data` one-time step |

## What's Deliberately NOT in v1

- **Cancellation** — subscription runs to completion. Add later if a real bot needs it.
- **Refunds** — pay upfront, get what you paid for.
- **Buyer-initiated parameter changes** — to change interval/ticks, hire again.
- **EAS attestations on tick** — left as `// TODO:` in `WebhookDeliveryService` so opt-in bots can wire into `ACP_EASIssuer`.
- **Pull-fallback endpoint** — webhook only.
- **Subscription "renewal" flow** — buyer hires again to renew.
- **Multi-tenant rate limiting** beyond the per-subscription `minIntervalSeconds` floor.
- **Web UI for ops** — query SQLite directly.
- **Worker leader election / multi-replica safety** — boilerplate assumes single replica per bot. Subscription state is in SQLite which is single-writer anyway. Multi-replica needs a different DB.

## Pinned stack versions

Same as BasicBot (per CLAUDE.md): .NET 10, Node 22, TypeScript ^5.7.2, `@virtuals-protocol/acp-node-v2 ^0.0.6`, `viem ^2.21.0`, `Microsoft.Data.Sqlite 9.0.*`, `Microsoft.AspNetCore.OpenApi 10.0.*`. No new dependencies added by this boilerplate.

## Smoke-Test Acceptance

Before marking the boilerplate "done", these MUST pass:

1. `cd BasicSubscriptionBot.Api && dotnet build` — zero warnings.
2. `cd BasicSubscriptionBot.Api && dotnet run` — both hosted services start cleanly, schema bootstraps, `/health` returns 200.
3. `curl -X POST http://localhost:5000/echo -H "Content-Type: application/json" -d '{"message":"hi"}'` — one-shot path returns 200.
4. `curl -X POST http://localhost:5000/subscriptions -H "Content-Type: application/json" -d '{"jobId":"test-1","buyerAgent":"0xabc...","offeringName":"tick_echo","requirement":{"message":"ping","webhookUrl":"http://localhost:9999/cb","intervalSeconds":60,"ticks":3}}'` (with `ALLOW_INSECURE_WEBHOOKS=true`) — returns subscription receipt JSON.
5. Local netcat-style listener at `localhost:9999/cb` receives 3 POSTs, ~60s apart, each with valid HMAC headers and monotonically increasing `tick` field.
6. `cd acp-v2 && npm install && npm run build` — clean tsc.
7. `npm run print-offerings` — both `echo` and `tick_echo` render correctly with their respective shapes (one-shot vs subscription block).

## Clone-and-Rename Checklist

Mirrors BasicBot's. Find/replace `BasicSubscriptionBot` → `MyNewBot` in: folder name, `.sln`, `.csproj`, namespaces, `package.json` `name`, `docker-compose.yml` service/container names, `README.md`. Then:

- Replace `tick_echo.ts` with the real subscription offering(s).
- Replace `tick_echo_state` table + `TickEchoRepository` + `TickEchoService` with the real domain logic.
- Update `TickExecutorService` to route by your offering name(s).
- Update `pricing.ts` and `appsettings.json`.
- Provision agent on app.virtuals.io and run `npm run print-offerings`.

If the bot doesn't need the one-shot path: delete `echo.ts`, `EchoRepository.cs`, `EchoService.cs`, `EchoRecord.cs`, `echo_records` table, and `/echo` endpoints. The subscription stack stands alone.

## Open Questions / Future Work

- v1.x: cancellation + pro-rata refund, if a real subscription bot needs it.
- v1.x: optional EAS attestation per tick — wire into `ACP_EASIssuer` via the existing `acp-shared` docker network (the same network DeFiEval uses to call TheMetaBot).
- v1.x: subscription renewal — buyer hires the same offering with `renewSubscriptionId` field, gets the same `subscriptionId` back with topped-up ticks.
- v2.x: webhook delivery via SSE/long-poll for buyers behind firewalls without inbound HTTPS.
