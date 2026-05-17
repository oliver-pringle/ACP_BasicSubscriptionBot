using BasicSubscriptionBot.Api.Models;
using Microsoft.Data.Sqlite;

namespace BasicSubscriptionBot.Api.Data;

public class SubscriptionRepository
{
    private const int SuspendThreshold = 3;
    private readonly Db _db;
    public SubscriptionRepository(Db db) => _db = db;

    public async Task InsertAsync(Subscription s)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO subscriptions
                (id, job_id, buyer_agent, offering_name, requirement_json,
                 webhook_url, webhook_secret, interval_seconds, ticks_purchased,
                 ticks_delivered, created_at, expires_at, last_run_at, next_run_at,
                 status, consecutive_failures, push_mode, stream_chain_id, stream_job_id)
            VALUES ($id, $job, $buyer, $off, $req,
                    $url, $sec, $iv, $tp,
                    $td, $ca, $ea, $lra, $nra,
                    $st, $cf, $pm, $sci, $sji);";
        cmd.Parameters.AddWithValue("$id", s.Id);
        cmd.Parameters.AddWithValue("$job", s.JobId);
        cmd.Parameters.AddWithValue("$buyer", s.BuyerAgent);
        cmd.Parameters.AddWithValue("$off", s.OfferingName);
        cmd.Parameters.AddWithValue("$req", s.RequirementJson);
        // webhook_url / webhook_secret are NOT NULL on disk (legacy schema).
        // For inJobStream subs, persist empty strings instead of null so the
        // constraint holds; SubscriptionRepository.Read projects empty → null
        // so callers see Subscription.WebhookUrl as nullable.
        cmd.Parameters.AddWithValue("$url", s.WebhookUrl ?? string.Empty);
        cmd.Parameters.AddWithValue("$sec", s.WebhookSecret ?? string.Empty);
        cmd.Parameters.AddWithValue("$iv", s.IntervalSeconds);
        cmd.Parameters.AddWithValue("$tp", s.TicksPurchased);
        cmd.Parameters.AddWithValue("$td", s.TicksDelivered);
        cmd.Parameters.AddWithValue("$ca", s.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$ea", s.ExpiresAt.ToString("O"));
        cmd.Parameters.AddWithValue("$lra", (object?)s.LastRunAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$nra", s.NextRunAt.ToString("O"));
        cmd.Parameters.AddWithValue("$st", s.Status);
        cmd.Parameters.AddWithValue("$cf", s.ConsecutiveFailures);
        cmd.Parameters.AddWithValue("$pm", s.PushMode);
        cmd.Parameters.AddWithValue("$sci", (object?)s.StreamChainId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sji", (object?)s.StreamJobId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Subscription?> GetByIdAsync(string id)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM subscriptions WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    public async Task<List<Subscription>> GetDueAsync(DateTime now, int limit)
    {
        var rows = new List<Subscription>();
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT * FROM subscriptions
            WHERE status='active' AND next_run_at <= $now
            ORDER BY next_run_at LIMIT $limit";
        cmd.Parameters.AddWithValue("$now", now.ToString("O"));
        cmd.Parameters.AddWithValue("$limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(Read(reader));
        return rows;
    }

    public async Task RecordTickResultAsync(
        string id,
        bool succeeded,
        DateTime lastRunAt,
        DateTime nextRunAt,
        bool completedSubscription)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        if (succeeded)
        {
            cmd.CommandText = @"
                UPDATE subscriptions
                SET ticks_delivered = ticks_delivered + 1,
                    last_run_at     = $lra,
                    next_run_at     = $nra,
                    consecutive_failures = 0,
                    status = CASE WHEN $done = 1 THEN 'completed' ELSE 'active' END
                WHERE id = $id";
        }
        else
        {
            cmd.CommandText = $@"
                UPDATE subscriptions
                SET ticks_delivered = ticks_delivered + 1,
                    last_run_at     = $lra,
                    next_run_at     = $nra,
                    consecutive_failures = consecutive_failures + 1,
                    status = CASE
                        WHEN consecutive_failures + 1 >= {SuspendThreshold} THEN 'suspended'
                        WHEN $done = 1 THEN 'completed'
                        ELSE 'active'
                    END
                WHERE id = $id";
        }
        cmd.Parameters.AddWithValue("$lra", lastRunAt.ToString("O"));
        cmd.Parameters.AddWithValue("$nra", nextRunAt.ToString("O"));
        cmd.Parameters.AddWithValue("$done", completedSubscription ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ResetConsecutiveFailuresAsync(string id)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE subscriptions SET consecutive_failures = 0 WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Subscription Read(SqliteDataReader r)
    {
        var url = r.GetString(r.GetOrdinal("webhook_url"));
        var sec = r.GetString(r.GetOrdinal("webhook_secret"));
        return new Subscription(
            Id: r.GetString(r.GetOrdinal("id")),
            JobId: r.GetString(r.GetOrdinal("job_id")),
            BuyerAgent: r.GetString(r.GetOrdinal("buyer_agent")),
            OfferingName: r.GetString(r.GetOrdinal("offering_name")),
            RequirementJson: r.GetString(r.GetOrdinal("requirement_json")),
            // Empty strings on disk represent NULL semantically — inJobStream subs
            // store empty to satisfy the legacy NOT NULL constraint.
            WebhookUrl: string.IsNullOrEmpty(url) ? null : url,
            WebhookSecret: string.IsNullOrEmpty(sec) ? null : sec,
            IntervalSeconds: r.GetInt32(r.GetOrdinal("interval_seconds")),
            TicksPurchased: r.GetInt32(r.GetOrdinal("ticks_purchased")),
            TicksDelivered: r.GetInt32(r.GetOrdinal("ticks_delivered")),
            CreatedAt: DateTime.Parse(r.GetString(r.GetOrdinal("created_at"))).ToUniversalTime(),
            ExpiresAt: DateTime.Parse(r.GetString(r.GetOrdinal("expires_at"))).ToUniversalTime(),
            LastRunAt: r.IsDBNull(r.GetOrdinal("last_run_at")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("last_run_at"))).ToUniversalTime(),
            NextRunAt: DateTime.Parse(r.GetString(r.GetOrdinal("next_run_at"))).ToUniversalTime(),
            Status: r.GetString(r.GetOrdinal("status")),
            ConsecutiveFailures: r.GetInt32(r.GetOrdinal("consecutive_failures")),
            PushMode: r.GetString(r.GetOrdinal("push_mode")),
            StreamChainId: r.IsDBNull(r.GetOrdinal("stream_chain_id")) ? null : r.GetInt32(r.GetOrdinal("stream_chain_id")),
            StreamJobId: r.IsDBNull(r.GetOrdinal("stream_job_id")) ? null : r.GetString(r.GetOrdinal("stream_job_id"))
        );
    }
}
