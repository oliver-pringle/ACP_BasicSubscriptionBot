using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Models;
using Xunit;

namespace BasicSubscriptionBot.Tests;

public class SubscriptionRepositoryTests
{
    private static Subscription Sample(string id, DateTime nextRun, string status = "active")
        => new(
            Id: id,
            JobId: $"job-{id}",
            BuyerAgent: "0xbuyer",
            OfferingName: "tick_echo",
            RequirementJson: "{}",
            WebhookUrl: "https://buyer.test/cb",
            WebhookSecret: "deadbeef",
            IntervalSeconds: 60,
            TicksPurchased: 5,
            TicksDelivered: 0,
            CreatedAt: DateTime.UtcNow,
            ExpiresAt: DateTime.UtcNow.AddHours(1),
            LastRunAt: null,
            NextRunAt: nextRun,
            Status: status,
            ConsecutiveFailures: 0,
            PushMode: "webhook",
            StreamChainId: null,
            StreamJobId: null
        );

    [Fact]
    public async Task Insert_then_get_returns_subscription()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var repo = new SubscriptionRepository(t.Db);

        var s = Sample("sub-1", DateTime.UtcNow.AddSeconds(60));
        await repo.InsertAsync(s);

        var fetched = await repo.GetByIdAsync("sub-1");
        Assert.NotNull(fetched);
        Assert.Equal("0xbuyer", fetched!.BuyerAgent);
        Assert.Equal(5, fetched.TicksPurchased);
    }

    [Fact]
    public async Task GetDue_returns_only_active_with_past_next_run()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var repo = new SubscriptionRepository(t.Db);

        await repo.InsertAsync(Sample("due-active", DateTime.UtcNow.AddSeconds(-10), "active"));
        await repo.InsertAsync(Sample("not-due", DateTime.UtcNow.AddSeconds(60), "active"));
        await repo.InsertAsync(Sample("suspended", DateTime.UtcNow.AddSeconds(-10), "suspended"));

        var due = await repo.GetDueAsync(DateTime.UtcNow, limit: 10);
        Assert.Single(due);
        Assert.Equal("due-active", due[0].Id);
    }

    [Fact]
    public async Task RecordTickResult_advances_ticks_and_resets_failures_on_success()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var repo = new SubscriptionRepository(t.Db);

        var s = Sample("sub-2", DateTime.UtcNow.AddSeconds(-1));
        await repo.InsertAsync(s);

        var nextRun = DateTime.UtcNow.AddSeconds(60);
        await repo.RecordTickResultAsync("sub-2", succeeded: true, lastRunAt: DateTime.UtcNow, nextRunAt: nextRun, completedSubscription: false);

        var f = await repo.GetByIdAsync("sub-2");
        Assert.Equal(1, f!.TicksDelivered);
        Assert.Equal(0, f.ConsecutiveFailures);
        Assert.Equal("active", f.Status);
    }

    [Fact]
    public async Task RecordTickResult_marks_completed_when_completedSubscription_true()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var repo = new SubscriptionRepository(t.Db);

        await repo.InsertAsync(Sample("sub-3", DateTime.UtcNow.AddSeconds(-1)) with { TicksPurchased = 1 });
        await repo.RecordTickResultAsync("sub-3", succeeded: true, lastRunAt: DateTime.UtcNow, nextRunAt: DateTime.UtcNow.AddSeconds(60), completedSubscription: true);

        var f = await repo.GetByIdAsync("sub-3");
        Assert.Equal("completed", f!.Status);
    }

    [Fact]
    public async Task RecordTickResult_increments_failures_on_failure_and_can_suspend()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var repo = new SubscriptionRepository(t.Db);

        await repo.InsertAsync(Sample("sub-4", DateTime.UtcNow.AddSeconds(-1)));

        await repo.RecordTickResultAsync("sub-4", false, DateTime.UtcNow, DateTime.UtcNow.AddSeconds(60), false);
        var after1 = await repo.GetByIdAsync("sub-4");
        Assert.Equal(1, after1!.ConsecutiveFailures);
        Assert.Equal("active", after1.Status);

        await repo.RecordTickResultAsync("sub-4", false, DateTime.UtcNow, DateTime.UtcNow.AddSeconds(60), false);
        await repo.RecordTickResultAsync("sub-4", false, DateTime.UtcNow, DateTime.UtcNow.AddSeconds(60), false);
        var after3 = await repo.GetByIdAsync("sub-4");
        Assert.Equal(3, after3!.ConsecutiveFailures);
        Assert.Equal("suspended", after3.Status);
    }

    [Fact]
    public async Task ResetConsecutiveFailures_zeros_the_counter()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var repo = new SubscriptionRepository(t.Db);

        await repo.InsertAsync(Sample("sub-5", DateTime.UtcNow.AddSeconds(60)));
        await repo.RecordTickResultAsync("sub-5", false, DateTime.UtcNow, DateTime.UtcNow.AddSeconds(60), false);
        await repo.ResetConsecutiveFailuresAsync("sub-5");

        var f = await repo.GetByIdAsync("sub-5");
        Assert.Equal(0, f!.ConsecutiveFailures);
    }
}
