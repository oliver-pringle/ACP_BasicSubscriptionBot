using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Middleware;
using BasicSubscriptionBot.Api.Models;
using BasicSubscriptionBot.Api.Services;
using BasicSubscriptionBot.Api.Workers;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Data
builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<EchoRepository>();
builder.Services.AddSingleton<SubscriptionRepository>();
builder.Services.AddSingleton<SubscriptionRunRepository>();
builder.Services.AddSingleton<TickEchoRepository>();

// Services
builder.Services.AddSingleton<EchoService>();
builder.Services.AddSingleton<SubscriptionService>();
builder.Services.AddSingleton<TickExecutorService>();
// WebhookDeliveryService is hardened against DNS-rebind TOCTOU + 3xx
// redirects (audit F1): the SocketsHttpHandler.ConnectCallback re-validates
// every resolved IPEndPoint against WebhookUrlValidator.IsConnectBlocked
// before the TCP connect, and AllowAutoRedirect=false ensures a 302 Location
// can't redirect a validated public webhook to 169.254.169.254 / 127.0.0.1
// / 10.0.0.0/8 etc. Lifted from ACP_OracleBot v0.7 / ACP_SolanaBot 2026-05-24.
builder.Services.AddHttpClient<WebhookDeliveryService>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectCallback   = WebhookConnectCallbacks.PinValidatedIp,
    });
// InJobStreamDeliveryService targets the sidecar's internal HTTP server at
// an operator-controlled URL (BASICSUBSCRIPTIONBOT_STREAM_PUSH_URL), NOT a
// buyer-supplied address — so the SSRF lane doesn't apply. Kept on the
// default handler.
builder.Services.AddHttpClient<InJobStreamDeliveryService>();

// Hosted workers
builder.Services.AddHostedService<TickSchedulerWorker>();
builder.Services.AddHostedService<RetryWorker>();

builder.Services.AddOpenApi();

const long MaxRequestBodyBytes = 256L * 1024L;
builder.Services.Configure<KestrelServerOptions>(o =>
{
    o.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
});

var app = builder.Build();

var db = app.Services.GetRequiredService<Db>();
await db.InitializeSchemaAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Fail-fast on legacy ALLOW_INSECURE_WEBHOOKS=true outside Development. The
// flag historically bypassed BOTH the https check AND the DNS+private-IP
// check — audit finding #3 split it into ALLOW_HTTP_WEBHOOKS +
// DISABLE_WEBHOOK_DNS_VALIDATION, but the legacy alias is retained for tests.
// In production, refusing to boot is safer than silently shipping the bypass.
var insecureWebhooks = string.Equals(
    builder.Configuration["ALLOW_INSECURE_WEBHOOKS"]
        ?? Environment.GetEnvironmentVariable("ALLOW_INSECURE_WEBHOOKS"),
    "true", StringComparison.OrdinalIgnoreCase);
var disableDnsValidation = string.Equals(
    builder.Configuration["DISABLE_WEBHOOK_DNS_VALIDATION"]
        ?? Environment.GetEnvironmentVariable("DISABLE_WEBHOOK_DNS_VALIDATION"),
    "true", StringComparison.OrdinalIgnoreCase);
if (!app.Environment.IsDevelopment())
{
    if (insecureWebhooks)
        throw new InvalidOperationException(
            "ALLOW_INSECURE_WEBHOOKS=true is only permitted in Development (legacy flag, " +
            "bypasses both https and DNS+private-IP checks). " +
            $"Current environment: {app.Environment.EnvironmentName}. " +
            "Use the granular ALLOW_HTTP_WEBHOOKS / DISABLE_WEBHOOK_DNS_VALIDATION flags " +
            "if you really need one of those behaviours, and never set DISABLE_WEBHOOK_DNS_VALIDATION outside tests.");
    if (disableDnsValidation)
        throw new InvalidOperationException(
            "DISABLE_WEBHOOK_DNS_VALIDATION=true is only permitted in Development. " +
            $"Current environment: {app.Environment.EnvironmentName}. " +
            "Without this check, an attacker can register a webhook whose hostname DNS-rebinds " +
            "to a private/metadata address — exactly the SSRF lane this flag exists to test.");
}

// Per-IP + per-X-API-Key sliding-window rate limit on heavy / write endpoints
// (audit F9). Placed BEFORE auth so unauthenticated floods are also throttled.
// Tunable via RateLimit:HeavyEndpointCapPerIp + RateLimit:HeavyEndpointCapPerApiKey.
app.UseMiddleware<RateLimitMiddleware>();

// Baseline security headers on every response (audit F10). OnStarting so
// downstream middleware can't accidentally erase them.
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        var p = ctx.Request.Path.Value ?? string.Empty;
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["Referrer-Policy"]        = "no-referrer";
        ctx.Response.Headers["X-Frame-Options"]        = "DENY";
        ctx.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        // /health + /v1/resources/* are deliberately CACHE-friendly — they're
        // intended for orchestrator pre-flight probes that benefit from a
        // short proxy TTL. Everything else is no-store.
        if (!p.StartsWith("/v1/resources/", StringComparison.Ordinal) && p != "/health")
            ctx.Response.Headers["Cache-Control"] = "no-store";
        return Task.CompletedTask;
    });
    await next();
});

// X-API-Key middleware. Required in any non-Development environment — a fail-
// open default plus a bad .env deploy or env-load failure would silently expose
// every endpoint. In Development the bot is still allowed to start without a
// key, with a loud warning, so local clones don't need configuration to boot.
var apiKey = builder.Configuration["ApiKey"]
    ?? Environment.GetEnvironmentVariable("BASICSUBSCRIPTIONBOT_API_KEY");
if (string.IsNullOrEmpty(apiKey))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "BASICSUBSCRIPTIONBOT_API_KEY is required in non-Development environments. " +
            $"Current environment: {app.Environment.EnvironmentName}. Set the env var " +
            "(or `ApiKey` in configuration) to a high-entropy random string.");
    }
    app.Logger.LogWarning(
        "BASICSUBSCRIPTIONBOT_API_KEY not set — Development mode only. " +
        "Endpoints accept all callers. Set the env var before any non-local deployment.");
}
else
{
    var expectedBytes = Encoding.UTF8.GetBytes(apiKey);
    app.Use(async (ctx, next) =>
    {
        // /health stays open for liveness/readiness probes.
        // /v1/resources/* stays open so buyer / orchestrator agents (Butler etc.)
        // can introspect the bot pre-hire — that's the whole point of Resources.
        var path = ctx.Request.Path.Value ?? string.Empty;
        if (path == "/health" || path.StartsWith("/v1/resources/", StringComparison.Ordinal)) { await next(); return; }
        if (!ctx.Request.Headers.TryGetValue("X-API-Key", out var provided))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("unauthorized");
            return;
        }
        var providedBytes = Encoding.UTF8.GetBytes(provided.ToString());
        if (providedBytes.Length != expectedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("unauthorized");
            return;
        }
        await next();
    });
    app.Logger.LogInformation("X-API-Key middleware enabled.");
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    time = DateTime.UtcNow.ToString("O")
}));

const int MaxMessageLength = 10_000;

app.MapPost("/echo", async (EchoRequest req, EchoService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.Message))
        return Results.BadRequest(new { error = "message is required" });
    if (req.Message.Length > MaxMessageLength)
        return Results.BadRequest(new { error = $"message exceeds {MaxMessageLength} character limit" });
    var record = await svc.RecordAsync(req.Message);
    return Results.Ok(record);
});

app.MapGet("/echo/{id:long}", async (long id, EchoService svc) =>
{
    var record = await svc.GetAsync(id);
    return record is null ? Results.NotFound() : Results.Ok(record);
});

app.MapPost("/subscriptions", async (CreateSubscriptionRequest req, SubscriptionService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.JobId))
        return Results.BadRequest(new { error = "jobId is required" });
    if (string.IsNullOrWhiteSpace(req.OfferingName))
        return Results.BadRequest(new { error = "offeringName is required" });
    try
    {
        var resp = await svc.CreateAsync(req);
        return Results.Ok(resp);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// GET /subscriptions/{id}
//   Default response = SubscriptionView.Minimal — excludes buyer-sensitive
//   fields (RequirementJson, WebhookUrl, BuyerAgent, StreamJobId). Any
//   X-API-Key-authenticated caller can poll status + counts; nothing buyer-
//   identifying leaks.
//
//   Pass header X-Subscription-Secret: <webhookSecret> for the FULL projection
//   (still excludes WebhookSecret itself — the caller proves they already
//   know it, no need to echo it back). The secret was delivered ONCE in the
//   ACP subscription receipt, so only the buyer holds it. Constant-time
//   compare against the stored secret. Closes audit F5.
//
//   inJobStream subscriptions have no webhookSecret — the full projection
//   is unreachable via this lane for them; only the minimal view is
//   returned regardless of headers. Operators on the box can hit the
//   SQLite file directly.
app.MapGet("/subscriptions/{id}", async (string id, HttpContext ctx, SubscriptionRepository repo) =>
{
    var sub = await repo.GetByIdAsync(id);
    if (sub is null) return Results.NotFound();

    if (ctx.Request.Headers.TryGetValue("X-Subscription-Secret", out var providedHeader) &&
        !string.IsNullOrEmpty(sub.WebhookSecret))
    {
        var providedBytes = Encoding.UTF8.GetBytes(providedHeader.ToString());
        var expectedBytes = Encoding.UTF8.GetBytes(sub.WebhookSecret);
        if (providedBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
        {
            return Results.Ok(SubscriptionView.Full(sub));
        }
        // Wrong secret — fall through to minimal view (not 401: the caller
        // already has X-API-Key, the header is just a privilege upgrade).
    }

    return Results.Ok(SubscriptionView.Minimal(sub));
});

// ACP v2 Resources — public, free, parameterised endpoints mirrored
// 1:1 with entries in acp-v2/src/resources.ts. Buyer / orchestrator agents
// (Butler etc.) call these BEFORE paying for an offering, so handlers must
// be cheap, side-effect-free, and stable. Add new routes here in lockstep
// with new entries in acp-v2/src/resources.ts; run `npm run print-resources`
// in acp-v2/ and paste each block into app.virtuals.io's Resources tab.
//
// Resources stay reachable even when the X-API-Key middleware is on —
// the middleware above whitelists /v1/resources/* alongside /health.
app.MapGet("/v1/resources/echoStatus", async (EchoRepository repo) =>
{
    var (count, lastAt) = await repo.GetStatusAsync();
    return Results.Ok(new
    {
        count,
        lastEchoAt = lastAt?.ToString("O")
    });
});

app.Run();
