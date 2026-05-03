using System.Security.Cryptography;
using System.Text.Json;
using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Models;

namespace BasicSubscriptionBot.Api.Services;

public class SubscriptionService
{
    private readonly SubscriptionRepository _subs;
    private readonly TickEchoRepository _tickEcho;

    public SubscriptionService(SubscriptionRepository subs, TickEchoRepository tickEcho)
    {
        _subs = subs;
        _tickEcho = tickEcho;
    }

    public async Task<CreateSubscriptionResponse> CreateAsync(CreateSubscriptionRequest req)
    {
        var ticks = AsInt(req.Requirement, "ticks");
        var interval = AsInt(req.Requirement, "intervalSeconds");
        var webhookUrl = AsString(req.Requirement, "webhookUrl");

        var id = Guid.NewGuid().ToString("N");
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = DateTime.UtcNow;
        var expiresAt = now.AddSeconds((long)interval * ticks);
        var nextRunAt = now.AddSeconds(interval);

        var sub = new Subscription(
            Id: id,
            JobId: req.JobId,
            BuyerAgent: req.BuyerAgent,
            OfferingName: req.OfferingName,
            RequirementJson: JsonSerializer.Serialize(req.Requirement),
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

        // Per-offering state
        switch (req.OfferingName)
        {
            case "tick_echo":
                await _tickEcho.InsertAsync(id, AsString(req.Requirement, "message"));
                break;
            default:
                throw new InvalidOperationException($"unknown offering: {req.OfferingName}");
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
