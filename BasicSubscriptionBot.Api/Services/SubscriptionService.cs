using System.Security.Cryptography;
using System.Text.Json;
using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Models;
using Microsoft.Extensions.Configuration;

namespace BasicSubscriptionBot.Api.Services;

public class SubscriptionService
{
    private readonly SubscriptionRepository _subs;
    private readonly TickEchoRepository _tickEcho;
    private readonly bool _allowInsecureWebhooks;

    // Bounds keep the worker pressure and DB rows sane for any clone. Override
    // per-bot only if a specific offering needs different shape.
    public const int MinIntervalSeconds      = 60;            // 1 / minute
    public const int MaxIntervalSeconds      = 86_400;        // 1 / day
    public const int MaxTicks                = 10_000;
    public const int MaxRequirementJsonBytes = 16 * 1024;     // 16 KB
    public static readonly TimeSpan MaxFutureWindow = TimeSpan.FromDays(90);

    public SubscriptionService(SubscriptionRepository subs, TickEchoRepository tickEcho, IConfiguration? cfg = null)
    {
        _subs = subs;
        _tickEcho = tickEcho;
        _allowInsecureWebhooks = string.Equals(
            cfg?["ALLOW_INSECURE_WEBHOOKS"], "true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CreateSubscriptionResponse> CreateAsync(CreateSubscriptionRequest req)
    {
        // Validate offering name FIRST — fail fast before any DB writes or secret gen.
        if (req.OfferingName != "tick_echo")
            throw new InvalidOperationException($"unknown offering: {req.OfferingName}");

        var ticks = AsInt(req.Requirement, "ticks");
        var interval = AsInt(req.Requirement, "intervalSeconds");
        var webhookUrl = AsString(req.Requirement, "webhookUrl");

        // Bounds checks BEFORE the secret gen + DB insert so callers get
        // actionable 4xx rather than orphan state.
        if (interval < MinIntervalSeconds || interval > MaxIntervalSeconds)
            throw new InvalidOperationException(
                $"intervalSeconds must be {MinIntervalSeconds}..{MaxIntervalSeconds}");
        if (ticks < 1 || ticks > MaxTicks)
            throw new InvalidOperationException($"ticks must be 1..{MaxTicks}");

        var windowSeconds = (long)interval * ticks;
        if (windowSeconds > (long)MaxFutureWindow.TotalSeconds)
            throw new InvalidOperationException(
                $"interval × ticks ({windowSeconds}s) exceeds {MaxFutureWindow.TotalDays} days");

        var requirementJson = JsonSerializer.Serialize(req.Requirement);
        if (System.Text.Encoding.UTF8.GetByteCount(requirementJson) > MaxRequirementJsonBytes)
            throw new InvalidOperationException(
                $"requirement JSON exceeds {MaxRequirementJsonBytes} bytes");

        // SSRF guard: reject loopback / RFC1918 / link-local / metadata / IPv6 ULA
        // unless ALLOW_INSECURE_WEBHOOKS=true (dev only).
        var urlCheck = WebhookUrlValidator.Validate(webhookUrl, _allowInsecureWebhooks);
        if (!urlCheck.Ok)
            throw new InvalidOperationException(urlCheck.Error!);

        var id = Guid.NewGuid().ToString("N");
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = DateTime.UtcNow;
        var expiresAt = now.AddSeconds(windowSeconds);
        var nextRunAt = now.AddSeconds(interval);

        var sub = new Subscription(
            Id: id,
            JobId: req.JobId,
            BuyerAgent: req.BuyerAgent,
            OfferingName: req.OfferingName,
            RequirementJson: requirementJson,
            WebhookUrl: webhookUrl,
            WebhookSecret: secret,
            IntervalSeconds: interval,
            TicksPurchased: ticks,
            TicksDelivered: 0,
            CreatedAt: now,
            ExpiresAt: expiresAt,
            LastRunAt: null,
            NextRunAt: nextRunAt,
            Status: "active",
            ConsecutiveFailures: 0
        );
        await _subs.InsertAsync(sub);

        // Per-offering state (offering name already validated above)
        if (req.OfferingName == "tick_echo")
        {
            await _tickEcho.InsertAsync(id, AsString(req.Requirement, "message"));
        }

        return new CreateSubscriptionResponse(id, secret, ticks, interval, expiresAt);
    }

    private static int AsInt(Dictionary<string, object> d, string key)
    {
        if (!d.TryGetValue(key, out var v)) throw new InvalidOperationException($"missing field: {key}");
        return v switch
        {
            int i => i,
            long l => (int)l,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
            string s when int.TryParse(s, out var p) => p,
            _ => throw new InvalidOperationException($"field {key} is not an int")
        };
    }

    private static string AsString(Dictionary<string, object> d, string key)
    {
        if (!d.TryGetValue(key, out var v)) throw new InvalidOperationException($"missing field: {key}");
        return v switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString()!,
            _ => throw new InvalidOperationException($"field {key} is not a string")
        };
    }
}
