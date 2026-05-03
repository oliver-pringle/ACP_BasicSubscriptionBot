using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Services;

namespace BasicSubscriptionBot.Api.Workers;

public class TickSchedulerWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private const int BatchSize = 100;
    private const int MaxConcurrent = 8;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<TickSchedulerWorker> _logger;

    public TickSchedulerWorker(IServiceScopeFactory scopes, ILogger<TickSchedulerWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TickSchedulerWorker started, polling every {Interval}", PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickOnceAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "TickScheduler tick failed; continuing"); }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var subs = scope.ServiceProvider.GetRequiredService<SubscriptionRepository>();
        var runs = scope.ServiceProvider.GetRequiredService<SubscriptionRunRepository>();
        var executor = scope.ServiceProvider.GetRequiredService<TickExecutorService>();
        var deliverer = scope.ServiceProvider.GetRequiredService<WebhookDeliveryService>();

        var due = await subs.GetDueAsync(DateTime.UtcNow, BatchSize);
        if (due.Count == 0) return;
        _logger.LogInformation("Tick batch: {Count} due subscriptions", due.Count);

        var sem = new SemaphoreSlim(MaxConcurrent);
        var tasks = due.Select(async sub =>
        {
            await sem.WaitAsync(ct);
            try { await ProcessSubscriptionAsync(sub, runs, subs, executor, deliverer, ct); }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    private async Task ProcessSubscriptionAsync(
        Models.Subscription sub,
        SubscriptionRunRepository runs,
        SubscriptionRepository subs,
        TickExecutorService executor,
        WebhookDeliveryService deliverer,
        CancellationToken ct)
    {
        var nextTickNumber = sub.TicksDelivered + 1;
        string payload;
        try { payload = await executor.ComputePayloadAsync(sub, nextTickNumber); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payload compute failed for sub {Id} tick {N}", sub.Id, nextTickNumber);
            return;
        }

        var runId = await runs.InsertPendingAsync(sub.Id, nextTickNumber, DateTime.UtcNow, payload);
        var result = await deliverer.DeliverAsync(sub, nextTickNumber, payload, ct);

        var nextRunAt = DateTime.UtcNow.AddSeconds(sub.IntervalSeconds);
        var completed = nextTickNumber >= sub.TicksPurchased;

        if (result.Ok)
        {
            await runs.MarkDeliveredAsync(runId, DateTime.UtcNow);
            await subs.RecordTickResultAsync(sub.Id, true, DateTime.UtcNow, nextRunAt, completed);
        }
        else
        {
            await runs.MarkRetryingAsync(runId, attempts: 1, nextAttemptAt: DateTime.UtcNow.Add(RetryBackoff.DelayFor(1)), lastError: result.Error ?? "unknown");
            await subs.RecordTickResultAsync(sub.Id, false, DateTime.UtcNow, nextRunAt, completed);
            _logger.LogWarning("Delivery failed for sub {Id} tick {N}: {Err}", sub.Id, nextTickNumber, result.Error);
        }
    }
}
