using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BasicSubscriptionBot.Api.Data;
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
builder.Services.AddHttpClient<WebhookDeliveryService>();

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

// Optional X-API-Key middleware (off by default)
var apiKey = builder.Configuration["ApiKey"]
    ?? Environment.GetEnvironmentVariable("BASICSUBSCRIPTIONBOT_API_KEY");
if (!string.IsNullOrEmpty(apiKey))
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
else
{
    app.Logger.LogWarning(
        "BASICSUBSCRIPTIONBOT_API_KEY not set — endpoints accept all callers. " +
        "Safe ONLY when the API stays on a private docker network.");
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

app.MapGet("/subscriptions/{id}", async (string id, SubscriptionRepository repo) =>
{
    var sub = await repo.GetByIdAsync(id);
    return sub is null ? Results.NotFound() : Results.Ok(sub);
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
