using System.Text.Json;
using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Models;

namespace BasicSubscriptionBot.Api.Services;

public class TickExecutorService
{
    private readonly TickEchoRepository _tickEcho;
    public TickExecutorService(TickEchoRepository tickEcho) => _tickEcho = tickEcho;

    public async Task<string> ComputePayloadAsync(Subscription sub, int tickNumber)
    {
        return sub.OfferingName switch
        {
            "tick_echo" => await ComputeTickEcho(sub, tickNumber),
            _ => throw new InvalidOperationException($"unknown offering: {sub.OfferingName}")
        };
    }

    private async Task<string> ComputeTickEcho(Subscription sub, int tick)
    {
        var state = await _tickEcho.GetAsync(sub.Id)
            ?? throw new InvalidOperationException($"tick_echo state missing for subscription {sub.Id}");
        var payload = new
        {
            subscriptionId = sub.Id,
            tick,
            totalTicks = sub.TicksPurchased,
            message = state.Message,
            deliveredAt = DateTime.UtcNow.ToString("O")
        };
        return JsonSerializer.Serialize(payload);
    }
}
