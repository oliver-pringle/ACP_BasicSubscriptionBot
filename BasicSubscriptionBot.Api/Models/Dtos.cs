namespace BasicSubscriptionBot.Api.Models;

public record CreateSubscriptionRequest(
    string JobId,
    string BuyerAgent,
    string OfferingName,
    Dictionary<string, object> Requirement,
    string? PushMode = null,
    int? StreamChainId = null,
    string? StreamJobId = null
);

public record CreateSubscriptionResponse(
    string SubscriptionId,
    string? WebhookSecret,
    int TicksPurchased,
    int IntervalSeconds,
    DateTime ExpiresAt,
    string PushMode
);

// Public projection of Subscription that omits WebhookSecret. The full
// Subscription record (which includes the HMAC key buyers use to verify
// callback signatures) must NEVER be returned over an unauthenticated route
// — anyone with subscriptionId would be able to forge tick deliveries.
public record SubscriptionView(
    string Id,
    string JobId,
    string BuyerAgent,
    string OfferingName,
    string RequirementJson,
    string? WebhookUrl,
    int IntervalSeconds,
    int TicksPurchased,
    int TicksDelivered,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? LastRunAt,
    DateTime NextRunAt,
    string Status,
    int ConsecutiveFailures,
    string PushMode,
    int? StreamChainId,
    string? StreamJobId
)
{
    public static SubscriptionView From(Subscription s) => new(
        s.Id, s.JobId, s.BuyerAgent, s.OfferingName, s.RequirementJson, s.WebhookUrl,
        s.IntervalSeconds, s.TicksPurchased, s.TicksDelivered, s.CreatedAt, s.ExpiresAt,
        s.LastRunAt, s.NextRunAt, s.Status, s.ConsecutiveFailures,
        s.PushMode, s.StreamChainId, s.StreamJobId);
}

public record EchoRequest(string Message);
