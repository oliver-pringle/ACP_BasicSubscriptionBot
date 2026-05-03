namespace BasicSubscriptionBot.Api.Models;

public record CreateSubscriptionRequest(
    string JobId,
    string BuyerAgent,
    string OfferingName,
    Dictionary<string, object> Requirement
);

public record CreateSubscriptionResponse(
    string SubscriptionId,
    string WebhookSecret,
    int TicksPurchased,
    int IntervalSeconds,
    DateTime ExpiresAt
);

public record EchoRequest(string Message);
