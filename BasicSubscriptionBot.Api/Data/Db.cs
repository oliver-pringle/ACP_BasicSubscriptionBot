using Microsoft.Data.Sqlite;

namespace BasicSubscriptionBot.Api.Data;

public class Db
{
    private readonly string _connectionString;

    public Db(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("Sqlite")
            ?? throw new InvalidOperationException("ConnectionStrings:Sqlite not configured");
    }

    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public async Task InitializeSchemaAsync()
    {
        await using var conn = OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS subscriptions (
                id                   TEXT PRIMARY KEY,
                job_id               TEXT NOT NULL UNIQUE,
                buyer_agent          TEXT NOT NULL,
                offering_name        TEXT NOT NULL,
                requirement_json     TEXT NOT NULL,
                webhook_url          TEXT NOT NULL,
                webhook_secret       TEXT NOT NULL,
                interval_seconds     INTEGER NOT NULL,
                ticks_purchased      INTEGER NOT NULL,
                ticks_delivered      INTEGER NOT NULL DEFAULT 0,
                created_at           TEXT NOT NULL,
                expires_at           TEXT NOT NULL,
                last_run_at          TEXT,
                next_run_at          TEXT NOT NULL,
                status               TEXT NOT NULL,
                consecutive_failures INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS ix_subs_due ON subscriptions(status, next_run_at);

            CREATE TABLE IF NOT EXISTS subscription_runs (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                subscription_id TEXT NOT NULL REFERENCES subscriptions(id),
                tick_number     INTEGER NOT NULL,
                scheduled_at    TEXT NOT NULL,
                payload_json    TEXT NOT NULL,
                delivery_status TEXT NOT NULL,
                attempts        INTEGER NOT NULL DEFAULT 0,
                next_attempt_at TEXT,
                last_attempt_at TEXT,
                last_error      TEXT,
                UNIQUE(subscription_id, tick_number)
            );
            CREATE INDEX IF NOT EXISTS ix_runs_retry ON subscription_runs(delivery_status, next_attempt_at);

            CREATE TABLE IF NOT EXISTS tick_echo_state (
                subscription_id TEXT PRIMARY KEY REFERENCES subscriptions(id),
                message         TEXT NOT NULL,
                created_at      TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS echo_records (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                message     TEXT    NOT NULL,
                received_at TEXT    NOT NULL
            );";
        await cmd.ExecuteNonQueryAsync();
    }
}
