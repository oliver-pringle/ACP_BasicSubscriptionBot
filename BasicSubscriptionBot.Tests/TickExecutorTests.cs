using System.Text.Json;
using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Models;
using BasicSubscriptionBot.Api.Services;
using Xunit;

namespace BasicSubscriptionBot.Tests;

public class TickExecutorTests
{
    private static async Task SeedSub(SubscriptionRepository repo, TickEchoRepository te, string id, string msg)
    {
        await repo.InsertAsync(new Subscription(
            id, $"job-{id}", "0x", "tick_echo", "{}", "https://x/cb", "sec",
            60, 5, 0, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), null,
            DateTime.UtcNow.AddSeconds(60), "active", 0));
        await te.InsertAsync(id, msg);
    }

    [Fact]
    public async Task Compute_tick_echo_returns_payload_with_message_and_tick_metadata()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var subs = new SubscriptionRepository(t.Db);
        var te = new TickEchoRepository(t.Db);
        await SeedSub(subs, te, "x", "ping");

        var executor = new TickExecutorService(te);
        var sub = (await subs.GetByIdAsync("x"))!;

        var json = await executor.ComputePayloadAsync(sub, tickNumber: 3);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("x", root.GetProperty("subscriptionId").GetString());
        Assert.Equal(3, root.GetProperty("tick").GetInt32());
        Assert.Equal(5, root.GetProperty("totalTicks").GetInt32());
        Assert.Equal("ping", root.GetProperty("message").GetString());
        Assert.True(root.TryGetProperty("deliveredAt", out _));
    }

    [Fact]
    public async Task Compute_unknown_offering_throws()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var executor = new TickExecutorService(new TickEchoRepository(t.Db));
        var sub = new Subscription(
            "x", "j", "0x", "unknown_offering", "{}", "https://x", "s",
            60, 5, 0, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), null,
            DateTime.UtcNow.AddSeconds(60), "active", 0);

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ComputePayloadAsync(sub, 1));
    }
}
