using System.Text.Json;
using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Models;
using BasicSubscriptionBot.Api.Services;
using Xunit;

namespace BasicSubscriptionBot.Tests;

public class SubscriptionServiceTests
{
    private static CreateSubscriptionRequest TickEchoReq(int ticks, int interval)
        => new(
            JobId: "job-x",
            BuyerAgent: "0xbuyer",
            OfferingName: "tick_echo",
            Requirement: new Dictionary<string, object>
            {
                ["message"]         = "ping",
                ["webhookUrl"]      = "https://buyer.test/cb",
                ["intervalSeconds"] = interval,
                ["ticks"]           = ticks
            }
        );

    [Fact]
    public async Task Create_tick_echo_inserts_subscription_and_state_and_returns_secret()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var svc = new SubscriptionService(
            new SubscriptionRepository(t.Db),
            new TickEchoRepository(t.Db));

        var resp = await svc.CreateAsync(TickEchoReq(ticks: 5, interval: 60));

        Assert.False(string.IsNullOrEmpty(resp.SubscriptionId));
        Assert.Equal(64, resp.WebhookSecret.Length);  // 32 bytes hex = 64 chars
        Assert.Equal(5, resp.TicksPurchased);
        Assert.Equal(60, resp.IntervalSeconds);
    }

    [Fact]
    public async Task Create_persists_message_to_tick_echo_state()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var subs = new SubscriptionRepository(t.Db);
        var te = new TickEchoRepository(t.Db);
        var svc = new SubscriptionService(subs, te);

        var resp = await svc.CreateAsync(TickEchoReq(3, 60));
        var state = await te.GetAsync(resp.SubscriptionId);
        Assert.NotNull(state);
        Assert.Equal("ping", state!.Message);
    }

    [Fact]
    public async Task Create_rejects_unknown_offering()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var svc = new SubscriptionService(
            new SubscriptionRepository(t.Db),
            new TickEchoRepository(t.Db));

        var bad = new CreateSubscriptionRequest(
            "j", "0x", "unknown",
            new Dictionary<string, object>
            {
                ["webhookUrl"] = "https://x/cb",
                ["intervalSeconds"] = 60,
                ["ticks"] = 1
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync(bad));
    }
}
