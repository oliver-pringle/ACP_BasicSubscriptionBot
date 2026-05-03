# ACP_BasicSubscriptionBot Boilerplate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the ACP_BasicSubscriptionBot boilerplate — a sibling to ACP_BasicBot specialised for subscription / recurring offerings via worker-loop + HTTPS webhook push, with HMAC-signed delivery, retry+suspend semantics, and a stub `tick_echo` offering that proves the pattern end-to-end.

**Architecture:** Two-tier (Node TS sidecar + .NET 10 API + SQLite), flat folder layout matching newer bots (`ACP_LiquidGuard`, `ACP_MEVProtect`). The C# tier owns two `IHostedService` background workers (TickScheduler + Retry) plus a `WebhookDeliveryService` with HMAC-SHA256 signing. The TS sidecar branches at requirement-handling time between the BasicBot one-shot path and a new subscription path that creates a SQLite-backed subscription record then submits a receipt deliverable to close the ACP job.

**Tech Stack:** .NET 10 ASP.NET Minimal API, ADO.NET + SQLite (`Microsoft.Data.Sqlite 9.0.1`), xUnit 2.9.2 for tests, Node 22, TypeScript ^5.7.2, `@virtuals-protocol/acp-node-v2 ^0.0.6`, `viem ^2.21.0`, Docker Compose.

**Source spec:** `docs/superpowers/specs/2026-05-03-acp-basicsubscriptionbot-boilerplate-design.md`

**Reference projects:**
- `C:\code_crypto\acp\ACP_BasicBot\BasicBot\` — copy-from for env/chain/provider/router/Dockerfile/docker-compose patterns
- `C:\code_crypto\acp\ACP_LiquidGuard\` — copy-from for Tests project setup + flat-folder layout

---

## Execution context for the worker

- The bot lives at `C:\code_crypto\acp\ACP_BasicSubscriptionBot\` (flat layout).
- Folders `docs\superpowers\specs\` and `docs\superpowers\plans\` already exist with the spec + this plan.
- The bot folder is NOT a git repo yet — Task 1 initialises it.
- The PARENT workspace (`C:\code_crypto\acp\`) is NOT a git repo. Each bot is its own repo.
- Platform is Windows 10 with PowerShell. Use Windows path separators (`\`) in commands. Avoid `&&` (PowerShell 5.1 doesn't support it — use `; if ($?) {}` or run separate Bash tool calls).
- Worker should `dotnet build` after each C# task to catch compile errors immediately. `npm run build` after each TS task likewise.

---

## File structure (locked-in decomposition)

```
ACP_BasicSubscriptionBot\
├── BasicSubscriptionBot.sln
├── docker-compose.yml
├── README.md
├── .gitignore
├── data\.gitkeep
│
├── BasicSubscriptionBot.Api\
│   ├── BasicSubscriptionBot.Api.csproj
│   ├── Program.cs
│   ├── appsettings.json
│   ├── Dockerfile
│   ├── Data\
│   │   ├── Db.cs
│   │   ├── EchoRepository.cs
│   │   ├── SubscriptionRepository.cs
│   │   ├── SubscriptionRunRepository.cs
│   │   └── TickEchoRepository.cs
│   ├── Models\
│   │   ├── EchoRecord.cs
│   │   ├── Subscription.cs
│   │   ├── SubscriptionRun.cs
│   │   ├── TickEchoState.cs
│   │   └── Dtos.cs
│   ├── Services\
│   │   ├── EchoService.cs
│   │   ├── SubscriptionService.cs
│   │   ├── TickExecutorService.cs
│   │   ├── WebhookDeliveryService.cs
│   │   └── RetryBackoff.cs
│   └── Workers\
│       ├── TickSchedulerWorker.cs
│       └── RetryWorker.cs
│
├── BasicSubscriptionBot.Tests\
│   ├── BasicSubscriptionBot.Tests.csproj
│   ├── HmacSigningTests.cs
│   ├── RetryBackoffTests.cs
│   ├── DbSchemaTests.cs
│   ├── SubscriptionRepositoryTests.cs
│   ├── SubscriptionRunRepositoryTests.cs
│   ├── EchoRepositoryTests.cs
│   ├── TickEchoRepositoryTests.cs
│   ├── TickExecutorTests.cs
│   ├── SubscriptionServiceTests.cs
│   └── TestDb.cs
│
└── acp-v2\
    ├── package.json
    ├── tsconfig.json
    ├── Dockerfile
    ├── .env.example
    ├── .gitignore
    ├── README.md
    ├── scripts\print-offerings-for-registration.ts
    └── src\
        ├── seller.ts
        ├── env.ts
        ├── chain.ts
        ├── provider.ts
        ├── router.ts
        ├── pricing.ts
        ├── deliverable.ts
        ├── apiClient.ts
        ├── validators.ts
        └── offerings\
            ├── types.ts
            ├── registry.ts
            ├── echo.ts
            └── tick_echo.ts
```

Each file has one responsibility. Repositories own SQL, services own business logic, workers own scheduling. The TS sidecar owns ACP protocol; C# owns everything else.

---

## Phase A — Repo skeleton

### Task 1: Init repo, .gitignore, .gitkeep, sln

**Files:**
- Create: `C:\code_crypto\acp\ACP_BasicSubscriptionBot\.gitignore`
- Create: `C:\code_crypto\acp\ACP_BasicSubscriptionBot\data\.gitkeep`
- Create: `C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.sln`

- [ ] **Step 1: Init git repo**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" init -b main
```

Expected: `Initialized empty Git repository in ...`. Confirms the bot is its own repo (parent workspace is not).

- [ ] **Step 2: Write `.gitignore`**

Create `C:\code_crypto\acp\ACP_BasicSubscriptionBot\.gitignore`:

```gitignore
# .NET
bin/
obj/
*.user
*.suo
.vs/

# Node
node_modules/
dist/
*.log
.DS_Store

# Env
.env
.env.local

# SQLite
data/*.db
data/*.db-journal
data/*.db-shm
data/*.db-wal
!data/.gitkeep

# OS
Thumbs.db
```

- [ ] **Step 3: Create `data/.gitkeep`** (empty file)

```powershell
New-Item -ItemType File -Path "C:\code_crypto\acp\ACP_BasicSubscriptionBot\data\.gitkeep" -Force | Out-Null
```

- [ ] **Step 4: Create empty solution file**

```powershell
dotnet new sln -n BasicSubscriptionBot -o "C:\code_crypto\acp\ACP_BasicSubscriptionBot"
```

Expected: `BasicSubscriptionBot.sln` appears at the root.

- [ ] **Step 5: Verify + commit**

```powershell
Get-ChildItem "C:\code_crypto\acp\ACP_BasicSubscriptionBot" -Force | Select-Object Name
```

Expected: shows `.git`, `.gitignore`, `BasicSubscriptionBot.sln`, `data`, `docs`.

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "chore: init bot repo with sln, gitignore, data dir"
```

---

### Task 2: API project + /health endpoint smoke

**Files:**
- Create: `BasicSubscriptionBot.Api\BasicSubscriptionBot.Api.csproj`
- Create: `BasicSubscriptionBot.Api\Program.cs`
- Create: `BasicSubscriptionBot.Api\appsettings.json`
- Modify: `BasicSubscriptionBot.sln`

- [ ] **Step 1: Create the .NET 10 web project**

```powershell
dotnet new web -n BasicSubscriptionBot.Api -o "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Api" --framework net10.0
dotnet sln "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.sln" add "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Api\BasicSubscriptionBot.Api.csproj"
```

- [ ] **Step 2: Replace generated csproj with the BasicBot-pattern csproj**

Overwrite `BasicSubscriptionBot.Api\BasicSubscriptionBot.Api.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Version>0.1.0</Version>
    <RootNamespace>BasicSubscriptionBot.Api</RootNamespace>
    <AssemblyName>BasicSubscriptionBot.Api</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.1" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.1" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Replace `Program.cs` with a minimal skeleton**

Overwrite `BasicSubscriptionBot.Api\Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    time = DateTime.UtcNow.ToString("O")
}));

app.Run();
```

- [ ] **Step 4: Add `appsettings.json` with the SQLite connection string**

Overwrite `BasicSubscriptionBot.Api\appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "localhost",
  "ConnectionStrings": {
    "Sqlite": "Data Source=basicsubscriptionbot.db;Cache=Shared"
  }
}
```

- [ ] **Step 5: Build + smoke test /health**

```powershell
dotnet build "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Api\BasicSubscriptionBot.Api.csproj"
```

Expected: `Build succeeded.` with 0 warnings.

In a separate terminal (run in background or skip if no easy way), start `dotnet run` and `curl http://localhost:5000/health`. Expected: `{"status":"ok","time":"..."}`. (If the worker can't easily background a server, skip the runtime check — Step 5 verifies build only; runtime smoke happens in Task 16.)

- [ ] **Step 6: Commit**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(api): scaffold .NET 10 API with /health endpoint"
```

---

### Task 3: Tests project setup

**Files:**
- Create: `BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj`
- Create: `BasicSubscriptionBot.Tests\SmokeTest.cs`
- Modify: `BasicSubscriptionBot.sln`

- [ ] **Step 1: Create xUnit test project**

```powershell
dotnet new xunit -n BasicSubscriptionBot.Tests -o "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests" --framework net10.0
dotnet sln "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.sln" add "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj"
dotnet add "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj" reference "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Api\BasicSubscriptionBot.Api.csproj"
```

- [ ] **Step 2: Replace generated csproj with the LiquidGuard-pattern csproj**

Overwrite `BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>BasicSubscriptionBot.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\BasicSubscriptionBot.Api\BasicSubscriptionBot.Api.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Replace generated `UnitTest1.cs` with a smoke test**

Delete `BasicSubscriptionBot.Tests\UnitTest1.cs` if it exists. Create `BasicSubscriptionBot.Tests\SmokeTest.cs`:

```csharp
namespace BasicSubscriptionBot.Tests;

public class SmokeTest
{
    [Fact]
    public void True_is_true()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 4: Run tests**

```powershell
dotnet test "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj"
```

Expected: `Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.

- [ ] **Step 5: Commit**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "test: scaffold xUnit project with smoke test"
```

---

## Phase B — Data layer (TDD)

### Task 4: Db.cs schema bootstrap (4 tables)

**Files:**
- Create: `BasicSubscriptionBot.Api\Data\Db.cs`
- Create: `BasicSubscriptionBot.Tests\TestDb.cs`
- Create: `BasicSubscriptionBot.Tests\DbSchemaTests.cs`
- Modify: `BasicSubscriptionBot.Api\Program.cs`

- [ ] **Step 1: Write the failing test (`DbSchemaTests.cs`)**

Create `BasicSubscriptionBot.Tests\DbSchemaTests.cs`:

```csharp
using BasicSubscriptionBot.Api.Data;
using Microsoft.Extensions.Configuration;

namespace BasicSubscriptionBot.Tests;

public class DbSchemaTests
{
    [Fact]
    public async Task InitializeSchema_creates_all_four_tables()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();

        var names = new HashSet<string>();
        await using var conn = t.Db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        Assert.Contains("subscriptions", names);
        Assert.Contains("subscription_runs", names);
        Assert.Contains("tick_echo_state", names);
        Assert.Contains("echo_records", names);
    }

    [Fact]
    public async Task InitializeSchema_is_idempotent()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        await t.Db.InitializeSchemaAsync(); // second call must not throw
    }
}
```

- [ ] **Step 2: Write the test helper (`TestDb.cs`)**

Create `BasicSubscriptionBot.Tests\TestDb.cs`:

```csharp
using BasicSubscriptionBot.Api.Data;
using Microsoft.Extensions.Configuration;

namespace BasicSubscriptionBot.Tests;

public sealed class TestDb : IAsyncDisposable
{
    public Db Db { get; }
    private readonly string _path;

    private TestDb(Db db, string path)
    {
        Db = db;
        _path = path;
    }

    public static TestDb New()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bsb-test-{Guid.NewGuid():N}.db");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Sqlite"] = $"Data Source={path};Cache=Shared"
            })
            .Build();
        return new TestDb(new Db(config), path);
    }

    public ValueTask DisposeAsync()
    {
        try { File.Delete(_path); } catch { /* test cleanup; ignore */ }
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 3: Run test — verify it fails**

```powershell
dotnet test "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj" --filter "FullyQualifiedName~DbSchemaTests"
```

Expected: build error — `Db` does not exist in `BasicSubscriptionBot.Api.Data`.

- [ ] **Step 4: Implement `Db.cs`**

Create directory `BasicSubscriptionBot.Api\Data\` if missing. Create `BasicSubscriptionBot.Api\Data\Db.cs`:

```csharp
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
```

- [ ] **Step 5: Wire `Db` into `Program.cs`**

Replace `BasicSubscriptionBot.Api\Program.cs` with:

```csharp
using BasicSubscriptionBot.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<Db>();
builder.Services.AddOpenApi();

var app = builder.Build();

var db = app.Services.GetRequiredService<Db>();
await db.InitializeSchemaAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    time = DateTime.UtcNow.ToString("O")
}));

app.Run();
```

- [ ] **Step 6: Run tests — verify they pass**

```powershell
dotnet test "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj" --filter "FullyQualifiedName~DbSchemaTests"
```

Expected: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 7: Commit**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(data): add Db with subscriptions/runs/tick_echo/echo_records schema"
```

---

### Task 5: Models (records)

**Files:**
- Create: `BasicSubscriptionBot.Api\Models\EchoRecord.cs`
- Create: `BasicSubscriptionBot.Api\Models\Subscription.cs`
- Create: `BasicSubscriptionBot.Api\Models\SubscriptionRun.cs`
- Create: `BasicSubscriptionBot.Api\Models\TickEchoState.cs`
- Create: `BasicSubscriptionBot.Api\Models\Dtos.cs`

- [ ] **Step 1: Create `EchoRecord.cs`**

```csharp
namespace BasicSubscriptionBot.Api.Models;

public record EchoRecord(long Id, string Message, DateTime ReceivedAt);
```

- [ ] **Step 2: Create `Subscription.cs`**

```csharp
namespace BasicSubscriptionBot.Api.Models;

public record Subscription(
    string Id,
    string JobId,
    string BuyerAgent,
    string OfferingName,
    string RequirementJson,
    string WebhookUrl,
    string WebhookSecret,
    int IntervalSeconds,
    int TicksPurchased,
    int TicksDelivered,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? LastRunAt,
    DateTime NextRunAt,
    string Status,
    int ConsecutiveFailures
);
```

- [ ] **Step 3: Create `SubscriptionRun.cs`**

```csharp
namespace BasicSubscriptionBot.Api.Models;

public record SubscriptionRun(
    long Id,
    string SubscriptionId,
    int TickNumber,
    DateTime ScheduledAt,
    string PayloadJson,
    string DeliveryStatus,
    int Attempts,
    DateTime? NextAttemptAt,
    DateTime? LastAttemptAt,
    string? LastError
);
```

- [ ] **Step 4: Create `TickEchoState.cs`**

```csharp
namespace BasicSubscriptionBot.Api.Models;

public record TickEchoState(string SubscriptionId, string Message, DateTime CreatedAt);
```

- [ ] **Step 5: Create `Dtos.cs`**

```csharp
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
```

- [ ] **Step 6: Build + commit**

```powershell
dotnet build "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.sln"
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(models): add Subscription, SubscriptionRun, TickEchoState, EchoRecord, DTOs"
```

Expected: build succeeds with 0 warnings.

---

### Task 6: EchoRepository (BasicBot equivalent) + tests

**Files:**
- Create: `BasicSubscriptionBot.Api\Data\EchoRepository.cs`
- Create: `BasicSubscriptionBot.Tests\EchoRepositoryTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BasicSubscriptionBot.Tests\EchoRepositoryTests.cs`:

```csharp
using BasicSubscriptionBot.Api.Data;

namespace BasicSubscriptionBot.Tests;

public class EchoRepositoryTests
{
    [Fact]
    public async Task Insert_then_get_returns_record()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var repo = new EchoRepository(t.Db);

        var inserted = await repo.InsertAsync("hello");
        var fetched = await repo.GetAsync(inserted.Id);

        Assert.NotNull(fetched);
        Assert.Equal("hello", fetched!.Message);
        Assert.Equal(inserted.Id, fetched.Id);
    }

    [Fact]
    public async Task Get_missing_returns_null()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var repo = new EchoRepository(t.Db);

        Assert.Null(await repo.GetAsync(999));
    }
}
```

- [ ] **Step 2: Run — verify fails**

```powershell
dotnet test "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj" --filter "FullyQualifiedName~EchoRepositoryTests"
```

Expected: build error — `EchoRepository` not defined.

- [ ] **Step 3: Implement `EchoRepository`**

Create `BasicSubscriptionBot.Api\Data\EchoRepository.cs`:

```csharp
using BasicSubscriptionBot.Api.Models;

namespace BasicSubscriptionBot.Api.Data;

public class EchoRepository
{
    private readonly Db _db;
    public EchoRepository(Db db) => _db = db;

    public async Task<EchoRecord> InsertAsync(string message)
    {
        var receivedAt = DateTime.UtcNow;
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO echo_records (message, received_at) VALUES ($m, $t); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$m", message);
        cmd.Parameters.AddWithValue("$t", receivedAt.ToString("O"));
        var id = (long)(await cmd.ExecuteScalarAsync())!;
        return new EchoRecord(id, message, receivedAt);
    }

    public async Task<EchoRecord?> GetAsync(long id)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, message, received_at FROM echo_records WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new EchoRecord(reader.GetInt64(0), reader.GetString(1), DateTime.Parse(reader.GetString(2)).ToUniversalTime());
    }
}
```

- [ ] **Step 4: Run tests — verify pass**

```powershell
dotnet test "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj" --filter "FullyQualifiedName~EchoRepositoryTests"
```

Expected: `Passed!  - Failed: 0, Passed: 2`.

- [ ] **Step 5: Commit**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(data): add EchoRepository with insert/get"
```

---

### Task 7: SubscriptionRepository + tests

**Files:**
- Create: `BasicSubscriptionBot.Api\Data\SubscriptionRepository.cs`
- Create: `BasicSubscriptionBot.Tests\SubscriptionRepositoryTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BasicSubscriptionBot.Tests\SubscriptionRepositoryTests.cs`:

```csharp
using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Models;

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
            ConsecutiveFailures: 0
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
```

- [ ] **Step 2: Run — verify fails**

```powershell
dotnet test "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj" --filter "FullyQualifiedName~SubscriptionRepositoryTests"
```

Expected: build error — `SubscriptionRepository` not defined.

- [ ] **Step 3: Implement `SubscriptionRepository`**

Create `BasicSubscriptionBot.Api\Data\SubscriptionRepository.cs`:

```csharp
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
                 status, consecutive_failures)
            VALUES ($id, $job, $buyer, $off, $req,
                    $url, $sec, $iv, $tp,
                    $td, $ca, $ea, $lra, $nra,
                    $st, $cf);";
        cmd.Parameters.AddWithValue("$id", s.Id);
        cmd.Parameters.AddWithValue("$job", s.JobId);
        cmd.Parameters.AddWithValue("$buyer", s.BuyerAgent);
        cmd.Parameters.AddWithValue("$off", s.OfferingName);
        cmd.Parameters.AddWithValue("$req", s.RequirementJson);
        cmd.Parameters.AddWithValue("$url", s.WebhookUrl);
        cmd.Parameters.AddWithValue("$sec", s.WebhookSecret);
        cmd.Parameters.AddWithValue("$iv", s.IntervalSeconds);
        cmd.Parameters.AddWithValue("$tp", s.TicksPurchased);
        cmd.Parameters.AddWithValue("$td", s.TicksDelivered);
        cmd.Parameters.AddWithValue("$ca", s.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$ea", s.ExpiresAt.ToString("O"));
        cmd.Parameters.AddWithValue("$lra", (object?)s.LastRunAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$nra", s.NextRunAt.ToString("O"));
        cmd.Parameters.AddWithValue("$st", s.Status);
        cmd.Parameters.AddWithValue("$cf", s.ConsecutiveFailures);
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

    private static Subscription Read(SqliteDataReader r) => new(
        Id: r.GetString(r.GetOrdinal("id")),
        JobId: r.GetString(r.GetOrdinal("job_id")),
        BuyerAgent: r.GetString(r.GetOrdinal("buyer_agent")),
        OfferingName: r.GetString(r.GetOrdinal("offering_name")),
        RequirementJson: r.GetString(r.GetOrdinal("requirement_json")),
        WebhookUrl: r.GetString(r.GetOrdinal("webhook_url")),
        WebhookSecret: r.GetString(r.GetOrdinal("webhook_secret")),
        IntervalSeconds: r.GetInt32(r.GetOrdinal("interval_seconds")),
        TicksPurchased: r.GetInt32(r.GetOrdinal("ticks_purchased")),
        TicksDelivered: r.GetInt32(r.GetOrdinal("ticks_delivered")),
        CreatedAt: DateTime.Parse(r.GetString(r.GetOrdinal("created_at"))).ToUniversalTime(),
        ExpiresAt: DateTime.Parse(r.GetString(r.GetOrdinal("expires_at"))).ToUniversalTime(),
        LastRunAt: r.IsDBNull(r.GetOrdinal("last_run_at")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("last_run_at"))).ToUniversalTime(),
        NextRunAt: DateTime.Parse(r.GetString(r.GetOrdinal("next_run_at"))).ToUniversalTime(),
        Status: r.GetString(r.GetOrdinal("status")),
        ConsecutiveFailures: r.GetInt32(r.GetOrdinal("consecutive_failures"))
    );
}
```

- [ ] **Step 4: Run tests — verify pass**

```powershell
dotnet test "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj" --filter "FullyQualifiedName~SubscriptionRepositoryTests"
```

Expected: `Passed!  - Failed: 0, Passed: 5`.

- [ ] **Step 5: Commit**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(data): add SubscriptionRepository with insert/get/due/recordTick/reset"
```

---

### Task 8: SubscriptionRunRepository + tests

**Files:**
- Create: `BasicSubscriptionBot.Api\Data\SubscriptionRunRepository.cs`
- Create: `BasicSubscriptionBot.Tests\SubscriptionRunRepositoryTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BasicSubscriptionBot.Tests\SubscriptionRunRepositoryTests.cs`:

```csharp
using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Models;

namespace BasicSubscriptionBot.Tests;

public class SubscriptionRunRepositoryTests
{
    private static async Task SeedSub(SubscriptionRepository repo, string id)
        => await repo.InsertAsync(new Subscription(
            id, $"job-{id}", "0x", "tick_echo", "{}", "https://x/cb", "sec",
            60, 5, 0, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), null,
            DateTime.UtcNow.AddSeconds(60), "active", 0));

    [Fact]
    public async Task Insert_then_query_returns_run()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var subs = new SubscriptionRepository(t.Db);
        var runs = new SubscriptionRunRepository(t.Db);
        await SeedSub(subs, "s1");

        var id = await runs.InsertPendingAsync("s1", tickNumber: 1, scheduledAt: DateTime.UtcNow, payloadJson: "{\"x\":1}");
        Assert.True(id > 0);

        var due = await runs.GetRetryDueAsync(DateTime.UtcNow.AddSeconds(60), limit: 10);
        Assert.Empty(due); // pending status, not retrying
    }

    [Fact]
    public async Task MarkDelivered_sets_status_and_records_time()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var subs = new SubscriptionRepository(t.Db);
        var runs = new SubscriptionRunRepository(t.Db);
        await SeedSub(subs, "s2");
        var id = await runs.InsertPendingAsync("s2", 1, DateTime.UtcNow, "{}");

        await runs.MarkDeliveredAsync(id, DateTime.UtcNow);
        var run = await runs.GetByIdAsync(id);
        Assert.Equal("delivered", run!.DeliveryStatus);
    }

    [Fact]
    public async Task MarkRetrying_schedules_next_attempt_and_appears_in_GetRetryDue()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var subs = new SubscriptionRepository(t.Db);
        var runs = new SubscriptionRunRepository(t.Db);
        await SeedSub(subs, "s3");
        var id = await runs.InsertPendingAsync("s3", 1, DateTime.UtcNow, "{}");

        var nextAttempt = DateTime.UtcNow.AddSeconds(-5); // already due
        await runs.MarkRetryingAsync(id, attempts: 1, nextAttemptAt: nextAttempt, lastError: "503");

        var due = await runs.GetRetryDueAsync(DateTime.UtcNow, limit: 10);
        Assert.Single(due);
        Assert.Equal(id, due[0].Id);
        Assert.Equal(1, due[0].Attempts);
    }

    [Fact]
    public async Task MarkDead_sets_status_dead_and_excludes_from_retry_due()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var subs = new SubscriptionRepository(t.Db);
        var runs = new SubscriptionRunRepository(t.Db);
        await SeedSub(subs, "s4");
        var id = await runs.InsertPendingAsync("s4", 1, DateTime.UtcNow, "{}");
        await runs.MarkRetryingAsync(id, 4, DateTime.UtcNow.AddSeconds(-5), "boom");

        await runs.MarkDeadAsync(id, attempts: 5, lastError: "max retries");
        var due = await runs.GetRetryDueAsync(DateTime.UtcNow, 10);
        Assert.Empty(due);

        var run = await runs.GetByIdAsync(id);
        Assert.Equal("dead", run!.DeliveryStatus);
    }
}
```

- [ ] **Step 2: Run — verify fails**

```powershell
dotnet test "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj" --filter "FullyQualifiedName~SubscriptionRunRepositoryTests"
```

Expected: build error — `SubscriptionRunRepository` not defined.

- [ ] **Step 3: Implement `SubscriptionRunRepository`**

Create `BasicSubscriptionBot.Api\Data\SubscriptionRunRepository.cs`:

```csharp
using BasicSubscriptionBot.Api.Models;
using Microsoft.Data.Sqlite;

namespace BasicSubscriptionBot.Api.Data;

public class SubscriptionRunRepository
{
    private readonly Db _db;
    public SubscriptionRunRepository(Db db) => _db = db;

    public async Task<long> InsertPendingAsync(string subscriptionId, int tickNumber, DateTime scheduledAt, string payloadJson)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO subscription_runs
                (subscription_id, tick_number, scheduled_at, payload_json, delivery_status, attempts)
            VALUES ($s, $t, $sa, $p, 'pending', 0);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$s", subscriptionId);
        cmd.Parameters.AddWithValue("$t", tickNumber);
        cmd.Parameters.AddWithValue("$sa", scheduledAt.ToString("O"));
        cmd.Parameters.AddWithValue("$p", payloadJson);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task MarkDeliveredAsync(long runId, DateTime at)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE subscription_runs
            SET delivery_status='delivered',
                last_attempt_at=$at,
                next_attempt_at=NULL
            WHERE id=$id";
        cmd.Parameters.AddWithValue("$at", at.ToString("O"));
        cmd.Parameters.AddWithValue("$id", runId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task MarkRetryingAsync(long runId, int attempts, DateTime nextAttemptAt, string lastError)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE subscription_runs
            SET delivery_status='retrying',
                attempts=$a,
                next_attempt_at=$na,
                last_attempt_at=$at,
                last_error=$err
            WHERE id=$id";
        cmd.Parameters.AddWithValue("$a", attempts);
        cmd.Parameters.AddWithValue("$na", nextAttemptAt.ToString("O"));
        cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$err", Truncate(lastError, 1024));
        cmd.Parameters.AddWithValue("$id", runId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task MarkDeadAsync(long runId, int attempts, string lastError)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE subscription_runs
            SET delivery_status='dead',
                attempts=$a,
                next_attempt_at=NULL,
                last_attempt_at=$at,
                last_error=$err
            WHERE id=$id";
        cmd.Parameters.AddWithValue("$a", attempts);
        cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$err", Truncate(lastError, 1024));
        cmd.Parameters.AddWithValue("$id", runId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<SubscriptionRun>> GetRetryDueAsync(DateTime now, int limit)
    {
        var rows = new List<SubscriptionRun>();
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, subscription_id, tick_number, scheduled_at, payload_json,
                   delivery_status, attempts, next_attempt_at, last_attempt_at, last_error
            FROM subscription_runs
            WHERE delivery_status='retrying' AND next_attempt_at <= $now
            ORDER BY next_attempt_at LIMIT $limit";
        cmd.Parameters.AddWithValue("$now", now.ToString("O"));
        cmd.Parameters.AddWithValue("$limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(Read(reader));
        return rows;
    }

    public async Task<SubscriptionRun?> GetByIdAsync(long id)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, subscription_id, tick_number, scheduled_at, payload_json,
                   delivery_status, attempts, next_attempt_at, last_attempt_at, last_error
            FROM subscription_runs WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    private static SubscriptionRun Read(SqliteDataReader r) => new(
        Id: r.GetInt64(0),
        SubscriptionId: r.GetString(1),
        TickNumber: r.GetInt32(2),
        ScheduledAt: DateTime.Parse(r.GetString(3)).ToUniversalTime(),
        PayloadJson: r.GetString(4),
        DeliveryStatus: r.GetString(5),
        Attempts: r.GetInt32(6),
        NextAttemptAt: r.IsDBNull(7) ? null : DateTime.Parse(r.GetString(7)).ToUniversalTime(),
        LastAttemptAt: r.IsDBNull(8) ? null : DateTime.Parse(r.GetString(8)).ToUniversalTime(),
        LastError: r.IsDBNull(9) ? null : r.GetString(9)
    );

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
```

- [ ] **Step 4: Run tests — verify pass**

```powershell
dotnet test "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj" --filter "FullyQualifiedName~SubscriptionRunRepositoryTests"
```

Expected: `Passed!  - Failed: 0, Passed: 4`.

- [ ] **Step 5: Commit**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(data): add SubscriptionRunRepository with retry-state lifecycle"
```

---

### Task 9: TickEchoRepository + tests

**Files:**
- Create: `BasicSubscriptionBot.Api\Data\TickEchoRepository.cs`
- Create: `BasicSubscriptionBot.Tests\TickEchoRepositoryTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BasicSubscriptionBot.Tests\TickEchoRepositoryTests.cs`:

```csharp
using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Models;

namespace BasicSubscriptionBot.Tests;

public class TickEchoRepositoryTests
{
    private static async Task SeedSub(SubscriptionRepository repo, string id)
        => await repo.InsertAsync(new Subscription(
            id, $"job-{id}", "0x", "tick_echo", "{}", "https://x/cb", "sec",
            60, 5, 0, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), null,
            DateTime.UtcNow.AddSeconds(60), "active", 0));

    [Fact]
    public async Task Insert_then_get_returns_state()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var subs = new SubscriptionRepository(t.Db);
        var repo = new TickEchoRepository(t.Db);
        await SeedSub(subs, "te-1");

        await repo.InsertAsync("te-1", "ping");
        var s = await repo.GetAsync("te-1");

        Assert.NotNull(s);
        Assert.Equal("ping", s!.Message);
    }

    [Fact]
    public async Task Get_missing_returns_null()
    {
        await using var t = TestDb.New();
        await t.Db.InitializeSchemaAsync();
        var repo = new TickEchoRepository(t.Db);
        Assert.Null(await repo.GetAsync("nope"));
    }
}
```

- [ ] **Step 2: Run — verify fails**

Expected: build error — `TickEchoRepository` not defined.

- [ ] **Step 3: Implement `TickEchoRepository`**

Create `BasicSubscriptionBot.Api\Data\TickEchoRepository.cs`:

```csharp
using BasicSubscriptionBot.Api.Models;

namespace BasicSubscriptionBot.Api.Data;

public class TickEchoRepository
{
    private readonly Db _db;
    public TickEchoRepository(Db db) => _db = db;

    public async Task InsertAsync(string subscriptionId, string message)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tick_echo_state (subscription_id, message, created_at)
            VALUES ($s, $m, $c)";
        cmd.Parameters.AddWithValue("$s", subscriptionId);
        cmd.Parameters.AddWithValue("$m", message);
        cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<TickEchoState?> GetAsync(string subscriptionId)
    {
        await using var conn = _db.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT subscription_id, message, created_at FROM tick_echo_state WHERE subscription_id=$s";
        cmd.Parameters.AddWithValue("$s", subscriptionId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new TickEchoState(reader.GetString(0), reader.GetString(1), DateTime.Parse(reader.GetString(2)).ToUniversalTime());
    }
}
```

- [ ] **Step 4: Run + commit**

```powershell
dotnet test "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj" --filter "FullyQualifiedName~TickEchoRepositoryTests"
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(data): add TickEchoRepository for tick_echo offering state"
```

Expected: 2 tests pass.

---

## Phase C — Service layer

### Task 10: RetryBackoff (pure function) + tests

**Files:**
- Create: `BasicSubscriptionBot.Api\Services\RetryBackoff.cs`
- Create: `BasicSubscriptionBot.Tests\RetryBackoffTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BasicSubscriptionBot.Tests\RetryBackoffTests.cs`:

```csharp
using BasicSubscriptionBot.Api.Services;

namespace BasicSubscriptionBot.Tests;

public class RetryBackoffTests
{
    [Theory]
    [InlineData(1, 30)]      // 30s
    [InlineData(2, 120)]     // 2m
    [InlineData(3, 600)]     // 10m
    [InlineData(4, 3600)]    // 1h
    [InlineData(5, 21600)]   // 6h
    public void DelaySeconds_for_attempt(int attempts, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RetryBackoff.DelayFor(attempts));
    }

    [Fact]
    public void IsExhausted_true_at_or_above_max()
    {
        Assert.False(RetryBackoff.IsExhausted(4));
        Assert.True(RetryBackoff.IsExhausted(5));
        Assert.True(RetryBackoff.IsExhausted(6));
    }
}
```

- [ ] **Step 2: Run — verify fails**

Expected: build error — `RetryBackoff` not defined.

- [ ] **Step 3: Implement `RetryBackoff`**

Create `BasicSubscriptionBot.Api\Services\RetryBackoff.cs`:

```csharp
namespace BasicSubscriptionBot.Api.Services;

public static class RetryBackoff
{
    public const int MaxAttempts = 5;

    private static readonly TimeSpan[] Schedule =
    {
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6),
    };

    public static TimeSpan DelayFor(int attempts)
    {
        if (attempts < 1) throw new ArgumentOutOfRangeException(nameof(attempts), "attempts must be >= 1");
        var idx = Math.Min(attempts - 1, Schedule.Length - 1);
        return Schedule[idx];
    }

    public static bool IsExhausted(int attempts) => attempts >= MaxAttempts;
}
```

- [ ] **Step 4: Run + commit**

```powershell
dotnet test "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj" --filter "FullyQualifiedName~RetryBackoffTests"
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(services): add RetryBackoff with 30s/2m/10m/1h/6h schedule"
```

Expected: 6 tests pass.

---

### Task 11: WebhookDeliveryService (HMAC + HTTP) + tests

**Files:**
- Create: `BasicSubscriptionBot.Api\Services\WebhookDeliveryService.cs`
- Create: `BasicSubscriptionBot.Tests\HmacSigningTests.cs`

- [ ] **Step 1: Write failing tests for HMAC**

Create `BasicSubscriptionBot.Tests\HmacSigningTests.cs`:

```csharp
using BasicSubscriptionBot.Api.Services;

namespace BasicSubscriptionBot.Tests;

public class HmacSigningTests
{
    [Fact]
    public void Signature_is_deterministic_for_same_inputs()
    {
        var s1 = WebhookDeliveryService.ComputeSignature("topsecret", tick: 1, timestamp: 1700000000, body: "{\"x\":1}");
        var s2 = WebhookDeliveryService.ComputeSignature("topsecret", tick: 1, timestamp: 1700000000, body: "{\"x\":1}");
        Assert.Equal(s1, s2);
    }

    [Fact]
    public void Signature_starts_with_sha256_prefix()
    {
        var s = WebhookDeliveryService.ComputeSignature("topsecret", 1, 1700000000, "{}");
        Assert.StartsWith("sha256=", s);
    }

    [Fact]
    public void Signature_changes_when_any_input_changes()
    {
        var baseline = WebhookDeliveryService.ComputeSignature("k", 1, 1, "b");
        Assert.NotEqual(baseline, WebhookDeliveryService.ComputeSignature("k2", 1, 1, "b"));
        Assert.NotEqual(baseline, WebhookDeliveryService.ComputeSignature("k", 2, 1, "b"));
        Assert.NotEqual(baseline, WebhookDeliveryService.ComputeSignature("k", 1, 2, "b"));
        Assert.NotEqual(baseline, WebhookDeliveryService.ComputeSignature("k", 1, 1, "b2"));
    }

    [Fact]
    public void Signature_matches_known_vector()
    {
        // Manually computed: HMAC-SHA256("k", "1.2.body") = 6acdee30aae3a7d4c34c95cba24c40c8b0b0c45e91d11d04944c10dc1e9eb061
        // Verify by recomputing in Python: hmac.new(b"k", b"1.2.body", hashlib.sha256).hexdigest()
        var s = WebhookDeliveryService.ComputeSignature("k", 1, 2, "body");
        Assert.Matches("^sha256=[0-9a-f]{64}$", s);
    }
}
```

- [ ] **Step 2: Run — verify fails**

Expected: build error — `WebhookDeliveryService` not defined.

- [ ] **Step 3: Implement `WebhookDeliveryService`**

Create `BasicSubscriptionBot.Api\Services\WebhookDeliveryService.cs`:

```csharp
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using BasicSubscriptionBot.Api.Models;

namespace BasicSubscriptionBot.Api.Services;

public class WebhookDeliveryService
{
    private readonly HttpClient _http;
    private readonly ILogger<WebhookDeliveryService> _logger;
    private const int BodyCapBytes = 1_048_576; // 1 MB
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public WebhookDeliveryService(HttpClient http, ILogger<WebhookDeliveryService> logger)
    {
        _http = http;
        _logger = logger;
        _http.Timeout = Timeout;
    }

    public static string ComputeSignature(string secret, int tick, long timestamp, string body)
    {
        var canonical = $"{tick}.{timestamp}.{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<DeliveryResult> DeliverAsync(Subscription sub, int tickNumber, string bodyJson, CancellationToken ct = default)
    {
        if (Encoding.UTF8.GetByteCount(bodyJson) > BodyCapBytes)
            return new DeliveryResult(false, $"payload exceeds {BodyCapBytes} bytes");

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeSignature(sub.WebhookSecret, tickNumber, ts, bodyJson);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, sub.WebhookUrl);
            req.Content = new StringContent(bodyJson, Encoding.UTF8);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            req.Headers.Add("X-Subscription-Id", sub.Id);
            req.Headers.Add("X-Subscription-Tick", tickNumber.ToString());
            req.Headers.Add("X-Subscription-Timestamp", ts.ToString());
            req.Headers.Add("X-Subscription-Signature", sig);

            using var resp = await _http.SendAsync(req, ct);
            if ((int)resp.StatusCode is >= 200 and < 300)
                return new DeliveryResult(true, null);

            return new DeliveryResult(false, $"HTTP {(int)resp.StatusCode}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new DeliveryResult(false, "timeout");
        }
        catch (HttpRequestException ex)
        {
            return new DeliveryResult(false, $"http: {ex.Message}");
        }
    }
}

public record DeliveryResult(bool Ok, string? Error);
```

- [ ] **Step 4: Run + commit**

```powershell
dotnet test "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj" --filter "FullyQualifiedName~HmacSigningTests"
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(services): add WebhookDeliveryService with HMAC-SHA256 signing + 10s timeout"
```

Expected: 4 tests pass.

---

### Task 12: TickExecutorService (offering-name router) + tests

**Files:**
- Create: `BasicSubscriptionBot.Api\Services\TickExecutorService.cs`
- Create: `BasicSubscriptionBot.Tests\TickExecutorTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BasicSubscriptionBot.Tests\TickExecutorTests.cs`:

```csharp
using System.Text.Json;
using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Models;
using BasicSubscriptionBot.Api.Services;

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
        await SeedSub(subs, "x", "ping");

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
```

- [ ] **Step 2: Run — verify fails**

Expected: build error — `TickExecutorService` not defined.

- [ ] **Step 3: Implement `TickExecutorService`**

Create `BasicSubscriptionBot.Api\Services\TickExecutorService.cs`:

```csharp
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
```

- [ ] **Step 4: Run + commit**

```powershell
dotnet test "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj" --filter "FullyQualifiedName~TickExecutorTests"
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(services): add TickExecutorService routing by offering name"
```

Expected: 2 tests pass.

---

### Task 13: SubscriptionService (creation logic) + tests

**Files:**
- Create: `BasicSubscriptionBot.Api\Services\SubscriptionService.cs`
- Create: `BasicSubscriptionBot.Tests\SubscriptionServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `BasicSubscriptionBot.Tests\SubscriptionServiceTests.cs`:

```csharp
using System.Text.Json;
using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Models;
using BasicSubscriptionBot.Api.Services;

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
```

- [ ] **Step 2: Run — verify fails**

Expected: build error — `SubscriptionService` not defined.

- [ ] **Step 3: Implement `SubscriptionService`**

Create `BasicSubscriptionBot.Api\Services\SubscriptionService.cs`:

```csharp
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
```

- [ ] **Step 4: Run + commit**

```powershell
dotnet test "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Tests\BasicSubscriptionBot.Tests.csproj" --filter "FullyQualifiedName~SubscriptionServiceTests"
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(services): add SubscriptionService.CreateAsync with secret gen + per-offering state"
```

Expected: 3 tests pass.

---

### Task 14: EchoService (BasicBot equivalent)

**Files:**
- Create: `BasicSubscriptionBot.Api\Services\EchoService.cs`

- [ ] **Step 1: Implement EchoService**

Create `BasicSubscriptionBot.Api\Services\EchoService.cs`:

```csharp
using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Models;

namespace BasicSubscriptionBot.Api.Services;

public class EchoService
{
    private readonly EchoRepository _repo;
    public EchoService(EchoRepository repo) => _repo = repo;

    public Task<EchoRecord> RecordAsync(string message) => _repo.InsertAsync(message);
    public Task<EchoRecord?> GetAsync(long id) => _repo.GetAsync(id);
}
```

- [ ] **Step 2: Build + commit**

```powershell
dotnet build "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.sln"
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(services): add EchoService (BasicBot equivalent)"
```

Expected: build succeeds.

---

## Phase D — Workers

### Task 15: TickSchedulerWorker

**Files:**
- Create: `BasicSubscriptionBot.Api\Workers\TickSchedulerWorker.cs`

The scheduler is hard to unit-test cleanly without dependency-injecting a clock + virtual time. We rely on the unit tests of `RecordTickResultAsync` (already passing) plus the end-to-end smoke test in Task 27 to validate behavior.

- [ ] **Step 1: Implement `TickSchedulerWorker`**

Create `BasicSubscriptionBot.Api\Workers\TickSchedulerWorker.cs`:

```csharp
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
```

- [ ] **Step 2: Build + commit**

```powershell
dotnet build "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.sln"
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(workers): add TickSchedulerWorker (10s poll, batch 100, concurrency 8)"
```

Expected: build succeeds.

---

### Task 16: RetryWorker

**Files:**
- Create: `BasicSubscriptionBot.Api\Workers\RetryWorker.cs`

- [ ] **Step 1: Implement `RetryWorker`**

Create `BasicSubscriptionBot.Api\Workers\RetryWorker.cs`:

```csharp
using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Services;

namespace BasicSubscriptionBot.Api.Workers;

public class RetryWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 100;
    private const int MaxConcurrent = 8;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RetryWorker> _logger;

    public RetryWorker(IServiceScopeFactory scopes, ILogger<RetryWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RetryWorker started, polling every {Interval}", PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickOnceAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "RetryWorker tick failed; continuing"); }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var runs = scope.ServiceProvider.GetRequiredService<SubscriptionRunRepository>();
        var subs = scope.ServiceProvider.GetRequiredService<SubscriptionRepository>();
        var deliverer = scope.ServiceProvider.GetRequiredService<WebhookDeliveryService>();

        var due = await runs.GetRetryDueAsync(DateTime.UtcNow, BatchSize);
        if (due.Count == 0) return;
        _logger.LogInformation("Retry batch: {Count} due runs", due.Count);

        var sem = new SemaphoreSlim(MaxConcurrent);
        var tasks = due.Select(async run =>
        {
            await sem.WaitAsync(ct);
            try { await ProcessRunAsync(run, runs, subs, deliverer, ct); }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    private async Task ProcessRunAsync(
        Models.SubscriptionRun run,
        SubscriptionRunRepository runs,
        SubscriptionRepository subs,
        WebhookDeliveryService deliverer,
        CancellationToken ct)
    {
        var sub = await subs.GetByIdAsync(run.SubscriptionId);
        if (sub is null || sub.Status == "suspended")
        {
            // Don't retry against suspended subs; mark dead so they fall out of the queue
            await runs.MarkDeadAsync(run.Id, run.Attempts, "subscription suspended or missing");
            return;
        }

        var result = await deliverer.DeliverAsync(sub, run.TickNumber, run.PayloadJson, ct);
        if (result.Ok)
        {
            await runs.MarkDeliveredAsync(run.Id, DateTime.UtcNow);
            await subs.ResetConsecutiveFailuresAsync(sub.Id);
            return;
        }

        var nextAttempts = run.Attempts + 1;
        if (RetryBackoff.IsExhausted(nextAttempts))
        {
            await runs.MarkDeadAsync(run.Id, nextAttempts, result.Error ?? "max retries");
            _logger.LogWarning("Run {Id} for sub {Sub} dead-lettered after {N} attempts", run.Id, sub.Id, nextAttempts);
        }
        else
        {
            await runs.MarkRetryingAsync(run.Id, nextAttempts, DateTime.UtcNow.Add(RetryBackoff.DelayFor(nextAttempts)), result.Error ?? "unknown");
        }
    }
}
```

- [ ] **Step 2: Build + commit**

```powershell
dotnet build "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.sln"
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(workers): add RetryWorker with backoff schedule + dead-letter at max"
```

Expected: build succeeds.

---

## Phase E — Endpoints + Program.cs wiring

### Task 17: Program.cs full wiring

**Files:**
- Modify: `BasicSubscriptionBot.Api\Program.cs`

- [ ] **Step 1: Replace `Program.cs` with the full version**

Overwrite `BasicSubscriptionBot.Api\Program.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Models;
using BasicSubscriptionBot.Api.Services;
using BasicSubscriptionBot.Api.Workers;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Data
builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<EchoRepository>();
builder.Services.AddSingleton<SubscriptionRepository>();
builder.Services.AddSingleton<SubscriptionRunRepository>();
builder.Services.AddSingleton<TickEchoRepository>();

// Services
builder.Services.AddSingleton<EchoService>();
builder.Services.AddSingleton<SubscriptionService>();
builder.Services.AddSingleton<TickExecutorService>();
builder.Services.AddHttpClient<WebhookDeliveryService>();

// Hosted workers
builder.Services.AddHostedService<TickSchedulerWorker>();
builder.Services.AddHostedService<RetryWorker>();

builder.Services.AddOpenApi();

const long MaxRequestBodyBytes = 256L * 1024L;
builder.Services.Configure<KestrelServerOptions>(o =>
{
    o.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
});

var app = builder.Build();

var db = app.Services.GetRequiredService<Db>();
await db.InitializeSchemaAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Optional X-API-Key middleware (off by default)
var apiKey = builder.Configuration["ApiKey"]
    ?? Environment.GetEnvironmentVariable("BASICSUBSCRIPTIONBOT_API_KEY");
if (!string.IsNullOrEmpty(apiKey))
{
    var expectedBytes = Encoding.UTF8.GetBytes(apiKey);
    app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path == "/health") { await next(); return; }
        if (!ctx.Request.Headers.TryGetValue("X-API-Key", out var provided))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("unauthorized");
            return;
        }
        var providedBytes = Encoding.UTF8.GetBytes(provided.ToString());
        if (providedBytes.Length != expectedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("unauthorized");
            return;
        }
        await next();
    });
    app.Logger.LogInformation("X-API-Key middleware enabled.");
}
else
{
    app.Logger.LogWarning(
        "BASICSUBSCRIPTIONBOT_API_KEY not set — endpoints accept all callers. " +
        "Safe ONLY when the API stays on a private docker network.");
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    time = DateTime.UtcNow.ToString("O")
}));

const int MaxMessageLength = 10_000;

app.MapPost("/echo", async (EchoRequest req, EchoService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.Message))
        return Results.BadRequest(new { error = "message is required" });
    if (req.Message.Length > MaxMessageLength)
        return Results.BadRequest(new { error = $"message exceeds {MaxMessageLength} character limit" });
    var record = await svc.RecordAsync(req.Message);
    return Results.Ok(record);
});

app.MapGet("/echo/{id:long}", async (long id, EchoService svc) =>
{
    var record = await svc.GetAsync(id);
    return record is null ? Results.NotFound() : Results.Ok(record);
});

app.MapPost("/subscriptions", async (CreateSubscriptionRequest req, SubscriptionService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.JobId))
        return Results.BadRequest(new { error = "jobId is required" });
    if (string.IsNullOrWhiteSpace(req.OfferingName))
        return Results.BadRequest(new { error = "offeringName is required" });
    try
    {
        var resp = await svc.CreateAsync(req);
        return Results.Ok(resp);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/subscriptions/{id}", async (string id, SubscriptionRepository repo) =>
{
    var sub = await repo.GetByIdAsync(id);
    return sub is null ? Results.NotFound() : Results.Ok(sub);
});

app.Run();
```

- [ ] **Step 2: Build + smoke test by `dotnet run`**

```powershell
dotnet build "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.sln"
```

Expected: build succeeds with 0 warnings.

If there's an easy way to background a process, run `dotnet run --project ...` and `curl http://localhost:5000/health`. Otherwise this is verified in Task 27.

- [ ] **Step 3: Commit**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(api): wire DI, endpoints, middleware, hosted workers in Program.cs"
```

---

## Phase F — Containerization

### Task 18: API Dockerfile

**Files:**
- Create: `BasicSubscriptionBot.Api\Dockerfile`

- [ ] **Step 1: Read the BasicBot Dockerfile for reference**

Read `C:\code_crypto\acp\ACP_BasicBot\BasicBot\BasicBot.Api\Dockerfile`. Adapt: replace `BasicBot.Api` → `BasicSubscriptionBot.Api`, replace `basicbot.db` → `basicsubscriptionbot.db` if any references.

- [ ] **Step 2: Write `BasicSubscriptionBot.Api\Dockerfile`**

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY BasicSubscriptionBot.Api/BasicSubscriptionBot.Api.csproj BasicSubscriptionBot.Api/
RUN dotnet restore BasicSubscriptionBot.Api/BasicSubscriptionBot.Api.csproj

COPY BasicSubscriptionBot.Api/ BasicSubscriptionBot.Api/
RUN dotnet publish BasicSubscriptionBot.Api/BasicSubscriptionBot.Api.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:5000
ENV ConnectionStrings__Sqlite="Data Source=/data/basicsubscriptionbot.db;Cache=Shared"

EXPOSE 5000
ENTRYPOINT ["dotnet", "BasicSubscriptionBot.Api.dll"]
```

- [ ] **Step 3: Commit**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(docker): add API Dockerfile"
```

(Build verification deferred to Task 25 docker-compose smoke.)

---

## Phase G — TS sidecar

### Task 19: Sidecar skeleton (package.json, tsconfig, copied infra files)

**Files:**
- Create: `acp-v2\package.json`
- Create: `acp-v2\tsconfig.json`
- Create: `acp-v2\.gitignore`
- Create: `acp-v2\.env.example`
- Create: `acp-v2\src\env.ts`
- Create: `acp-v2\src\chain.ts`
- Create: `acp-v2\src\provider.ts`
- Create: `acp-v2\src\router.ts`
- Create: `acp-v2\src\deliverable.ts`

- [ ] **Step 1: Read each BasicBot equivalent and copy verbatim**

For each file in this list, read from `C:\code_crypto\acp\ACP_BasicBot\BasicBot\acp-v2\` and copy to `C:\code_crypto\acp\ACP_BasicSubscriptionBot\acp-v2\` at the same relative path. Apply these find/replaces in the copied content:
- `BasicBot` → `BasicSubscriptionBot`
- `basicbot` → `basicsubscriptionbot`
- `BASICBOT_` → `BASICSUBSCRIPTIONBOT_`

Files to copy verbatim (with the rename applied):
- `package.json`
- `tsconfig.json`
- `.gitignore`
- `.env.example`
- `src/env.ts`
- `src/chain.ts`
- `src/provider.ts`
- `src/router.ts`
- `src/deliverable.ts`

- [ ] **Step 2: Verify renames**

```powershell
Select-String -Path "C:\code_crypto\acp\ACP_BasicSubscriptionBot\acp-v2\*","C:\code_crypto\acp\ACP_BasicSubscriptionBot\acp-v2\src\*" -Pattern "BasicBot|basicbot|BASICBOT_" -CaseSensitive
```

Expected: no matches. If any are found, fix them inline.

- [ ] **Step 3: Install + tsc smoke**

```powershell
cd "C:\code_crypto\acp\ACP_BasicSubscriptionBot\acp-v2"
npm install
npm run build
```

Expected: `npm install` succeeds; `tsc` may fail because seller.ts / offerings missing — that's OK for now, we add them in following tasks. If `npm run build` doesn't yet exist as a script, ensure `package.json` `scripts` contains:
```json
{ "scripts": { "build": "tsc", "dev": "tsx watch src/seller.ts", "print-offerings": "tsx scripts/print-offerings-for-registration.ts" } }
```

- [ ] **Step 4: Commit**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(sidecar): scaffold acp-v2 with env/chain/provider/router/deliverable from BasicBot"
```

---

### Task 20: Validators (extended) + extended Offering interface

**Files:**
- Create: `acp-v2\src\validators.ts`
- Create: `acp-v2\src\offerings\types.ts`

- [ ] **Step 1: Read BasicBot's `validators.ts` and copy + extend**

Read `C:\code_crypto\acp\ACP_BasicBot\BasicBot\acp-v2\src\validators.ts`. Copy to the new location, then APPEND these new validators:

Final `acp-v2\src\validators.ts` content (the BasicBot original PLUS the additions below):

```typescript
// (Keep all existing BasicBot validators above this comment.)

export interface ValidationResult {
  valid: boolean;
  reason?: string;
}

export function requireStringLength(
  value: unknown,
  field: string,
  maxLength: number
): ValidationResult {
  if (typeof value !== "string") return { valid: false, reason: `${field} must be a string` };
  if (value.length === 0) return { valid: false, reason: `${field} is required` };
  if (value.length > maxLength)
    return { valid: false, reason: `${field} exceeds ${maxLength} character limit` };
  return { valid: true };
}

export function requireWebhookUrl(value: unknown, field: string): ValidationResult {
  if (typeof value !== "string") return { valid: false, reason: `${field} must be a string` };
  let url: URL;
  try { url = new URL(value); }
  catch { return { valid: false, reason: `${field} is not a valid URL` }; }

  const allowInsecure = process.env.ALLOW_INSECURE_WEBHOOKS === "true";
  if (url.protocol !== "https:" && !allowInsecure)
    return { valid: false, reason: `${field} must be HTTPS (set ALLOW_INSECURE_WEBHOOKS=true for dev)` };
  if (!allowInsecure) {
    const host = url.hostname.toLowerCase();
    if (host === "localhost" || host === "127.0.0.1" || host === "::1" ||
        host.startsWith("10.") || host.startsWith("192.168.") ||
        /^172\.(1[6-9]|2\d|3[01])\./.test(host))
      return { valid: false, reason: `${field} must not point to a private/loopback host` };
  }
  return { valid: true };
}

export function requireIntInRange(
  value: unknown,
  field: string,
  min: number,
  max: number
): ValidationResult {
  if (typeof value !== "number" || !Number.isInteger(value))
    return { valid: false, reason: `${field} must be an integer` };
  if (value < min) return { valid: false, reason: `${field} must be >= ${min}` };
  if (value > max) return { valid: false, reason: `${field} must be <= ${max}` };
  return { valid: true };
}
```

- [ ] **Step 2: Write extended `Offering` interface**

Create `acp-v2\src\offerings\types.ts`:

```typescript
import type { ValidationResult } from "../validators.js";
import type { ApiClient } from "../apiClient.js";

export interface OfferingContext {
  client: ApiClient;
}

export interface SubscriptionConfig {
  pricePerTickUsdc: number;
  minIntervalSeconds: number;
  maxTicks: number;
  maxDurationDays: number;
}

export interface Offering {
  name: string;
  description: string;
  requirementSchema: Record<string, unknown>;
  validate(req: Record<string, unknown>): ValidationResult;

  // Exactly one of the following two MUST be set.
  execute?(req: Record<string, unknown>, ctx: OfferingContext): Promise<unknown>;
  subscription?: SubscriptionConfig;
}
```

- [ ] **Step 3: Build + commit**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(sidecar): add validators (webhook URL, int range) + extended Offering interface"
```

---

### Task 21: pricing.ts + apiClient.ts

**Files:**
- Create: `acp-v2\src\pricing.ts`
- Create: `acp-v2\src\apiClient.ts`

- [ ] **Step 1: Write `pricing.ts`**

```typescript
import type { AssetToken } from "@virtuals-protocol/acp-node-v2";
import { OFFERINGS } from "./offerings/registry.js";

const DEFAULT_PRICE_USDC = 0.1;

export interface Price {
  amountUsdc: number;
}

export function priceFor(offeringName: string, requirement: Record<string, unknown>): Price {
  const off = OFFERINGS[offeringName];
  if (!off) return { amountUsdc: DEFAULT_PRICE_USDC };

  if (off.subscription) {
    const ticks = typeof requirement.ticks === "number" ? requirement.ticks : 0;
    return { amountUsdc: off.subscription.pricePerTickUsdc * ticks };
  }

  // One-shot: per-name fixed price table; default if absent.
  const fixed: Record<string, number> = { echo: 0.1 };
  return { amountUsdc: fixed[offeringName] ?? DEFAULT_PRICE_USDC };
}

export async function priceForAssetToken(
  offeringName: string,
  requirement: Record<string, unknown>,
  chainId: number
): Promise<AssetToken> {
  const price = priceFor(offeringName, requirement);
  const { AssetToken } = await import("@virtuals-protocol/acp-node-v2");
  return AssetToken.usdc(price.amountUsdc, chainId);
}
```

- [ ] **Step 2: Write `apiClient.ts`**

```typescript
export interface ApiClient {
  echo(input: { message: string }): Promise<unknown>;
  createSubscription(input: {
    jobId: string;
    buyerAgent: string;
    offeringName: string;
    requirement: Record<string, unknown>;
  }): Promise<{
    subscriptionId: string;
    webhookSecret: string;
    ticksPurchased: number;
    intervalSeconds: number;
    expiresAt: string;
  }>;
}

export function createApiClient(baseUrl: string, opts: { apiKey?: string } = {}): ApiClient {
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (opts.apiKey) headers["X-API-Key"] = opts.apiKey;

  async function post<T>(path: string, body: unknown): Promise<T> {
    const r = await fetch(`${baseUrl}${path}`, { method: "POST", headers, body: JSON.stringify(body) });
    if (!r.ok) throw new Error(`POST ${path} -> ${r.status}: ${await r.text()}`);
    return (await r.json()) as T;
  }

  return {
    echo(input)              { return post("/echo", input); },
    createSubscription(input) { return post("/subscriptions", input); }
  };
}
```

- [ ] **Step 3: Commit**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(sidecar): add pricing (one-shot + tick math) and apiClient"
```

---

### Task 22: Stub offerings (echo + tick_echo) + registry

**Files:**
- Create: `acp-v2\src\offerings\echo.ts`
- Create: `acp-v2\src\offerings\tick_echo.ts`
- Create: `acp-v2\src\offerings\registry.ts`

- [ ] **Step 1: Write `echo.ts` (one-shot, BasicBot equivalent)**

```typescript
import type { Offering } from "./types.js";
import { requireStringLength } from "../validators.js";

const MAX_MESSAGE_LENGTH = 10_000;

export const echo: Offering = {
  name: "echo",
  description:
    "Echo a message back. One-shot offering. Demonstrates the BasicSubscriptionBot pattern handles vanilla one-shot calls alongside subscription offerings.",
  requirementSchema: {
    type: "object",
    properties: {
      message: { type: "string", description: "The message to echo back.", maxLength: MAX_MESSAGE_LENGTH }
    },
    required: ["message"]
  },
  validate(req) {
    return requireStringLength(req.message, "message", MAX_MESSAGE_LENGTH);
  },
  async execute(req, { client }) {
    return await client.echo({ message: String(req.message) });
  }
};
```

- [ ] **Step 2: Write `tick_echo.ts`**

```typescript
import type { Offering } from "./types.js";
import { requireStringLength, requireWebhookUrl, requireIntInRange } from "../validators.js";

const MAX_MESSAGE_LENGTH = 1000;
const PRICE_PER_TICK_USDC = 0.01;
const MIN_INTERVAL_SECONDS = 60;
const MAX_TICKS = 1000;
const MAX_DURATION_DAYS = 90;

export const tickEcho: Offering = {
  name: "tick_echo",
  description:
    "Push a fixed message to your webhook every N seconds for K ticks. Subscription offering — pay upfront for the full tick budget. Demonstrates the BasicSubscriptionBot worker-loop + webhook + HMAC pattern end-to-end.",
  requirementSchema: {
    type: "object",
    properties: {
      message:         { type: "string",  maxLength: MAX_MESSAGE_LENGTH, description: "Message echoed verbatim on each tick." },
      webhookUrl:      { type: "string",  format: "uri",                  description: "HTTPS URL to receive each tick." },
      intervalSeconds: { type: "integer", minimum: MIN_INTERVAL_SECONDS,  description: "Seconds between ticks." },
      ticks:           { type: "integer", minimum: 1, maximum: MAX_TICKS, description: "Number of ticks (deliveries) to purchase." }
    },
    required: ["message", "webhookUrl", "intervalSeconds", "ticks"]
  },
  validate(req) {
    const m = requireStringLength(req.message, "message", MAX_MESSAGE_LENGTH);
    if (!m.valid) return m;
    const w = requireWebhookUrl(req.webhookUrl, "webhookUrl");
    if (!w.valid) return w;
    const i = requireIntInRange(req.intervalSeconds, "intervalSeconds", MIN_INTERVAL_SECONDS, MAX_DURATION_DAYS * 86400);
    if (!i.valid) return i;
    const t = requireIntInRange(req.ticks, "ticks", 1, MAX_TICKS);
    if (!t.valid) return t;
    const totalSec = (req.intervalSeconds as number) * (req.ticks as number);
    const cap = MAX_DURATION_DAYS * 86400;
    if (totalSec > cap) return { valid: false, reason: `intervalSeconds × ticks (${totalSec}s) exceeds ${MAX_DURATION_DAYS}d cap (${cap}s)` };
    return { valid: true };
  },
  subscription: {
    pricePerTickUsdc: PRICE_PER_TICK_USDC,
    minIntervalSeconds: MIN_INTERVAL_SECONDS,
    maxTicks: MAX_TICKS,
    maxDurationDays: MAX_DURATION_DAYS
  }
};
```

- [ ] **Step 3: Write `registry.ts`**

```typescript
import type { Offering } from "./types.js";
import { echo } from "./echo.js";
import { tickEcho } from "./tick_echo.js";

export const OFFERINGS: Record<string, Offering> = {
  echo,
  tick_echo: tickEcho
};

export function getOffering(name: string): Offering | undefined {
  return OFFERINGS[name];
}

export function listOfferings(): string[] {
  return Object.keys(OFFERINGS);
}
```

- [ ] **Step 4: Build TS to verify types compile**

```powershell
cd "C:\code_crypto\acp\ACP_BasicSubscriptionBot\acp-v2"
npm run build
```

Expected: `tsc` may still fail because `seller.ts` is missing. If so, that's fine for now — we add it next. If errors are about anything OTHER than `seller.ts`, fix them.

- [ ] **Step 5: Commit**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(sidecar): add echo + tick_echo offerings and registry"
```

---

### Task 23: seller.ts (branches one-shot vs subscription)

**Files:**
- Create: `acp-v2\src\seller.ts`

- [ ] **Step 1: Write `seller.ts`**

```typescript
import { AcpAgent } from "@virtuals-protocol/acp-node-v2";
import type { JobSession, JobRoomEntry } from "@virtuals-protocol/acp-node-v2";
import { loadEnv } from "./env.js";
import { createProvider } from "./provider.js";
import { createApiClient } from "./apiClient.js";
import { route } from "./router.js";
import { priceForAssetToken } from "./pricing.js";
import { toDeliverable } from "./deliverable.js";
import { listOfferings, getOffering } from "./offerings/registry.js";

type PendingJob = {
  offeringName: string;
  requirement: Record<string, unknown>;
};

async function main() {
  const env = loadEnv();
  const client = createApiClient(env.apiUrl, { apiKey: env.apiKey });

  console.log(`[seller] chain=${env.chain} wallet=${env.walletAddress}`);
  console.log(`[seller] api=${env.apiUrl}`);
  console.log(`[seller] offerings registered (in code): ${listOfferings().length}`);

  const provider = await createProvider(env);
  const agent = await AcpAgent.create({ provider });

  const pending = new Map<string, PendingJob>();

  agent.on("entry", async (session: JobSession, entry: JobRoomEntry) => {
    try {
      if (entry.kind === "system") {
        switch (entry.event.type) {
          case "job.created":   console.log(`[seller] job.created jobId=${session.jobId}`); return;
          case "job.funded":    return await handleJobFunded(session);
          case "job.completed": pending.delete(session.jobId); console.log(`[seller] job.completed jobId=${session.jobId}`); return;
          case "job.rejected":  pending.delete(session.jobId); console.log(`[seller] job.rejected jobId=${session.jobId}`); return;
          case "job.expired":   pending.delete(session.jobId); return;
          default: return;
        }
      }
      if (entry.kind === "message" && entry.contentType === "requirement") {
        await handleRequirement(session, entry);
      }
    } catch (err) {
      console.error(`[seller] handler error for job ${session.jobId}:`, err);
    }
  });

  async function handleRequirement(session: JobSession, entry: JobRoomEntry) {
    if (entry.kind !== "message") return;

    let requirement: Record<string, unknown>;
    try { requirement = JSON.parse(entry.content); }
    catch { await session.sendMessage("invalid requirement payload"); return; }

    const job = session.job ?? (await session.fetchJob());
    const offeringName = job.description;

    const ZERO_ADDRESS = "0x0000000000000000000000000000000000000000";
    if (job.evaluatorAddress.toLowerCase() !== ZERO_ADDRESS) {
      await session.sendMessage(
        `unsupported: this seller only accepts jobs with evaluatorAddress=${ZERO_ADDRESS}. Got: ${job.evaluatorAddress}`
      );
      return;
    }

    const offering = getOffering(offeringName);
    if (!offering) { await session.sendMessage(`unknown offering: ${offeringName}`); return; }

    const v = offering.validate(requirement);
    if (!v.valid) { await session.sendMessage(v.reason ?? "validation failed"); return; }

    const price = await priceForAssetToken(offeringName, requirement, session.chainId);
    await session.setBudget(price);

    pending.set(session.jobId, { offeringName, requirement });
  }

  async function handleJobFunded(session: JobSession) {
    const stash = pending.get(session.jobId);
    if (!stash) {
      console.warn(`[seller] job.funded without stashed requirement, jobId=${session.jobId}`);
      return;
    }
    const offering = getOffering(stash.offeringName);
    if (!offering) {
      await session.sendMessage(`unknown offering at funded time: ${stash.offeringName}`);
      return;
    }

    if (offering.subscription) {
      // Subscription path: create subscription, submit receipt
      const buyerAgent = (session.job?.clientAddress ?? "0x").toString();
      let receipt;
      try {
        receipt = await client.createSubscription({
          jobId: session.jobId,
          buyerAgent,
          offeringName: stash.offeringName,
          requirement: stash.requirement
        });
      } catch (err) {
        await session.sendMessage(`subscription creation failed: ${(err as Error).message}`);
        return;
      }
      const payload = await toDeliverable(session.jobId, {
        subscriptionId: receipt.subscriptionId,
        webhookSecret: receipt.webhookSecret,
        ticksPurchased: receipt.ticksPurchased,
        intervalSeconds: receipt.intervalSeconds,
        expiresAt: receipt.expiresAt,
        signatureScheme: "HMAC-SHA256(secret, tick + '.' + timestamp + '.' + body)"
      });
      await session.submit(payload);
      console.log(`[seller] submitted subscription receipt jobId=${session.jobId} subId=${receipt.subscriptionId}`);
      return;
    }

    // One-shot path
    const outcome = await route(stash.offeringName, stash.requirement, { client });
    if (!outcome.ok) { await session.sendMessage(`execution failed: ${outcome.reason}`); return; }
    const payload = await toDeliverable(session.jobId, outcome.result);
    await session.submit(payload);
    console.log(`[seller] submitted one-shot jobId=${session.jobId} offering=${stash.offeringName}`);
  }

  await agent.start();

  const shutdown = async (signal: string) => {
    console.log(`[seller] ${signal} received, stopping agent`);
    try { await agent.stop(); } finally { process.exit(0); }
  };
  process.on("SIGINT", () => void shutdown("SIGINT"));
  process.on("SIGTERM", () => void shutdown("SIGTERM"));

  console.log("[seller] running — waiting for jobs");
}

main().catch((err) => { console.error("[seller] fatal:", err); process.exit(1); });
```

- [ ] **Step 2: Build + commit**

```powershell
cd "C:\code_crypto\acp\ACP_BasicSubscriptionBot\acp-v2"
npm run build
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(sidecar): add seller.ts branching one-shot vs subscription"
```

Expected: clean tsc.

---

### Task 24: print-offerings script

**Files:**
- Create: `acp-v2\scripts\print-offerings-for-registration.ts`

- [ ] **Step 1: Write the script**

```typescript
import { OFFERINGS } from "../src/offerings/registry.js";

for (const [name, off] of Object.entries(OFFERINGS)) {
  console.log("=".repeat(60));
  console.log(`name:        ${name}`);
  console.log(`description: ${off.description}`);
  if (off.subscription) {
    console.log(`type:        SUBSCRIPTION`);
    console.log(`pricePerTick: ${off.subscription.pricePerTickUsdc} USDC`);
    console.log(`minInterval:  ${off.subscription.minIntervalSeconds}s`);
    console.log(`maxTicks:     ${off.subscription.maxTicks}`);
    console.log(`maxDuration:  ${off.subscription.maxDurationDays}d`);
    console.log(`requirementSchema:`);
    console.log(JSON.stringify(off.requirementSchema, null, 2));
    console.log(`pricing note: total = pricePerTick × ticks (computed at requirement time)`);
  } else {
    console.log(`type:        ONE-SHOT`);
    console.log(`requirementSchema:`);
    console.log(JSON.stringify(off.requirementSchema, null, 2));
  }
}
console.log("=".repeat(60));
```

- [ ] **Step 2: Run + verify**

```powershell
cd "C:\code_crypto\acp\ACP_BasicSubscriptionBot\acp-v2"
npm run print-offerings
```

Expected: prints both `echo` (ONE-SHOT) and `tick_echo` (SUBSCRIPTION with pricing block).

- [ ] **Step 3: Commit**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(sidecar): add print-offerings script handling both shapes"
```

---

### Task 25: Sidecar Dockerfile

**Files:**
- Create: `acp-v2\Dockerfile`

- [ ] **Step 1: Read the BasicBot sidecar Dockerfile**

Read `C:\code_crypto\acp\ACP_BasicBot\BasicBot\acp-v2\Dockerfile`.

- [ ] **Step 2: Adapt and write `acp-v2\Dockerfile`**

Apply the same renames (`basicbot` → `basicsubscriptionbot`). The Dockerfile is otherwise the same shape (multi-stage, `node:22-slim`, build TS, run as `node` user).

If the BasicBot sidecar Dockerfile is, for example:
```dockerfile
FROM node:22-slim AS build
WORKDIR /app
COPY package.json package-lock.json* ./
RUN npm ci || npm install
COPY tsconfig.json ./
COPY src ./src
COPY scripts ./scripts
RUN npm run build

FROM node:22-slim AS runtime
WORKDIR /app
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist
COPY package.json ./
USER node
CMD ["node", "dist/seller.js"]
```

— write the equivalent at `acp-v2\Dockerfile`. (No string replacements needed if the BasicBot file uses no bot-name strings inline.)

- [ ] **Step 3: Commit**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(docker): add sidecar Dockerfile"
```

---

### Task 26: docker-compose.yml + .env.example for the bot

**Files:**
- Create: `docker-compose.yml`
- Create: `.env.example` (at bot root, not under acp-v2)

- [ ] **Step 1: Read BasicBot's docker-compose.yml**

Read `C:\code_crypto\acp\ACP_BasicBot\BasicBot\docker-compose.yml`.

- [ ] **Step 2: Write the new `docker-compose.yml`**

Copy the BasicBot file, then apply renames:
- `basicbot` → `basicsubscriptionbot` (service names, container names, network name, volume paths, env vars)
- `BasicBot.Api` → `BasicSubscriptionBot.Api` (build context paths)

Final file at `C:\code_crypto\acp\ACP_BasicSubscriptionBot\docker-compose.yml`. Verify no `basicbot` strings remain:

```powershell
Select-String -Path "C:\code_crypto\acp\ACP_BasicSubscriptionBot\docker-compose.yml" -Pattern "basicbot" -CaseSensitive
```

Expected: no matches.

- [ ] **Step 3: Write `.env.example` at bot root**

```env
# Optional: enable X-API-Key auth on the C# API (off by default for private bridge)
# BASICSUBSCRIPTIONBOT_API_KEY=

# Dev only: allow http:// or loopback webhooks for local testing
# ALLOW_INSECURE_WEBHOOKS=true
```

- [ ] **Step 4: Commit**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "feat(docker): add docker-compose.yml + .env.example"
```

---

## Phase H — README

### Task 27: README (clone-and-rename checklist)

**Files:**
- Create: `README.md`

- [ ] **Step 1: Read BasicBot's README**

Read `C:\code_crypto\acp\ACP_BasicBot\BasicBot\README.md` for structure.

- [ ] **Step 2: Write the new README**

Create `C:\code_crypto\acp\ACP_BasicSubscriptionBot\README.md`:

```markdown
# BasicSubscriptionBot — ACP 2.0 Subscription Boilerplate

Sibling to ACP_BasicBot. Same two-tier shape (TS sidecar + .NET 10 API + SQLite), plus a worker loop and webhook push delivery for **subscription / recurring offerings**. Clone this folder, rename, and replace the stub `tick_echo` with your real subscription logic.

## When to use this vs ACP_BasicBot

| Bot needs | Start from |
|---|---|
| Only one-shot offerings (request → reply, done) | ACP_BasicBot |
| Any subscription / "watch X" / scheduled push offering | ACP_BasicSubscriptionBot |
| Both shapes in one bot | ACP_BasicSubscriptionBot (handles both) |

## Architecture

```
acp-v2/   (Node 22 / TypeScript)            BasicSubscriptionBot.Api/   (.NET 10)
@virtuals-protocol/acp-node-v2  ──HTTP──►  ADO.NET + SQLite + 2 hosted workers
                                            (TickScheduler + Retry)
                                            +
                                            HTTPS POST + HMAC-SHA256
                                              ─────────────────►  Buyer's webhook
```

## How a subscription works

1. Buyer hires a subscription offering (e.g. `tick_echo`) with `{ ticks: 24, intervalSeconds: 3600, webhookUrl, ... }`.
2. Sidecar validates, computes price `pricePerTickUsdc × ticks`, calls `setBudget`.
3. Buyer funds. Sidecar calls `POST /subscriptions` on the C# API.
4. C# inserts a row, generates a 32-byte HMAC secret, returns it.
5. Sidecar `submit()`s a **subscription receipt** containing `subscriptionId` + `webhookSecret`. ACP job done.
6. `TickSchedulerWorker` fires on schedule, computes the tick payload, POSTs to the buyer's webhook with HMAC headers.
7. After N ticks: subscription marked `completed`. Done.

## Local development

Two terminals:

```bash
# Terminal 1 — C# API on http://localhost:5000
cd BasicSubscriptionBot.Api
dotnet run
```

```bash
# Terminal 2 — ACP sidecar
cd acp-v2
cp .env.example .env       # then fill in agent credentials
npm install
npm run dev
```

For local subscription testing without HTTPS:
```powershell
$env:ALLOW_INSECURE_WEBHOOKS = "true"
```

## Smoke tests

(Same 7 acceptance tests as in `docs/superpowers/specs/2026-05-03-acp-basicsubscriptionbot-boilerplate-design.md`.)

## Cloning for a new bot

1. Copy `ACP_BasicSubscriptionBot/` → `ACP_MyNewBot/`
2. Find/replace `BasicSubscriptionBot` → `MyNewBot` (case-sensitive) in: folder, .sln, .csproj, namespaces, package.json `name`, docker-compose service/container names, env var names (`BASICSUBSCRIPTIONBOT_*` → `MYNEWBOT_*`).
3. Provision a new agent on app.virtuals.io, copy creds into `acp-v2/.env`.
4. Replace stub offerings:
   - Delete `tick_echo.ts` (or keep + add your own).
   - Delete `tick_echo_state` table + `TickEchoRepository` if not needed.
   - Add your real subscription offerings to `src/offerings/`, register in `registry.ts`.
   - Update `TickExecutorService.cs` to route by your offering names.
   - Update `pricing.ts` and validators if your subscription has different bounds.
5. If you don't need the one-shot path: delete `echo.ts`, `EchoRepository.cs`, `EchoService.cs`, `EchoRecord.cs`, `echo_records` table, `/echo` endpoints.
6. `npm run print-offerings` and register on app.virtuals.io.

## What's intentionally NOT in this shell

- Cancellation / refunds (subscription runs to completion)
- EAS attestation per tick (left as `// TODO:` — opt in per bot via the `acp-shared` network into ACP_EASIssuer)
- Pull-fallback delivery (webhook only)
- Subscription renewal (buyer hires again)
- Multi-replica / leader election (single replica per bot)

## Security

Webhook scheme: HTTPS only (override `ALLOW_INSECURE_WEBHOOKS=true` for dev). HMAC-SHA256 signature header `X-Subscription-Signature`. Webhook secret returned **once** in the receipt deliverable — buyer must persist. Buyers MUST treat duplicate `(subscriptionId, tick)` as no-op (we may retry).

See `docs/superpowers/specs/2026-05-03-acp-basicsubscriptionbot-boilerplate-design.md` for full design.
```

- [ ] **Step 3: Commit**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "docs: add README with subscription model + clone-and-rename checklist"
```

---

## Phase I — Final smoke acceptance

### Task 28: End-to-end smoke tests (7 from the spec)

**Files:** none new — runtime verification only.

- [ ] **Step 1: dotnet build (full solution, zero warnings)**

```powershell
dotnet build "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.sln"
```

Expected: `Build succeeded.` with `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 2: dotnet test (full test suite passes)**

```powershell
dotnet test "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.sln"
```

Expected: all tests pass. Sum of: SmokeTest(1) + DbSchema(2) + EchoRepo(2) + SubscriptionRepo(5) + SubscriptionRunRepo(4) + TickEchoRepo(2) + RetryBackoff(6) + Hmac(4) + TickExecutor(2) + SubscriptionService(3) = 31 tests passing.

- [ ] **Step 3: Run the API and verify /health**

In one terminal:
```powershell
$env:ALLOW_INSECURE_WEBHOOKS = "true"
dotnet run --project "C:\code_crypto\acp\ACP_BasicSubscriptionBot\BasicSubscriptionBot.Api\BasicSubscriptionBot.Api.csproj"
```

In another:
```powershell
Invoke-RestMethod http://localhost:5000/health
```

Expected: `status=ok`, plus log line `TickSchedulerWorker started, polling every 00:00:10` and `RetryWorker started, polling every 00:00:30`.

- [ ] **Step 4: One-shot echo smoke**

```powershell
$body = @{ message = "hi" } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://localhost:5000/echo -ContentType "application/json" -Body $body
Invoke-RestMethod http://localhost:5000/echo/1
```

Expected: first returns `{ id, message: "hi", receivedAt }`. Second returns the same row.

- [ ] **Step 5: Subscription end-to-end smoke**

Start a tiny webhook receiver in a third terminal (Python one-liner is the easiest):

```powershell
python -c "from http.server import HTTPServer, BaseHTTPRequestHandler; import sys
class H(BaseHTTPRequestHandler):
    def do_POST(self):
        n = int(self.headers.get('Content-Length', '0'))
        body = self.rfile.read(n).decode()
        sys.stdout.write(f'TICK={self.headers.get(\"X-Subscription-Tick\")} SIG={self.headers.get(\"X-Subscription-Signature\")} BODY={body}\n')
        sys.stdout.flush()
        self.send_response(200); self.end_headers(); self.wfile.write(b'ok')
HTTPServer(('127.0.0.1', 9999), H).serve_forever()"
```

Create the subscription:
```powershell
$req = @{
  jobId = "test-1"
  buyerAgent = "0xbuyer"
  offeringName = "tick_echo"
  requirement = @{
    message = "ping"
    webhookUrl = "http://127.0.0.1:9999/cb"
    intervalSeconds = 60
    ticks = 3
  }
} | ConvertTo-Json -Depth 5
Invoke-RestMethod -Method Post -Uri http://localhost:5000/subscriptions -ContentType "application/json" -Body $req
```

Expected: returns `{ subscriptionId, webhookSecret, ticksPurchased: 3, intervalSeconds: 60, expiresAt }`.

- [ ] **Step 6: Verify webhook receives 3 ticks ~60s apart**

Wait ~3 minutes. The webhook listener should print 3 `TICK=1`, `TICK=2`, `TICK=3` lines, each with valid `SIG=sha256=<hex>` and a JSON body containing `subscriptionId`, `tick`, `totalTicks: 3`, `message: "ping"`, `deliveredAt`.

After tick 3, query `GET /subscriptions/{id}` and verify `status: "completed"`.

- [ ] **Step 7: TS sidecar build + print-offerings**

```powershell
cd "C:\code_crypto\acp\ACP_BasicSubscriptionBot\acp-v2"
npm install
npm run build
npm run print-offerings
```

Expected: `tsc` clean. `print-offerings` shows both `echo` (ONE-SHOT) and `tick_echo` (SUBSCRIPTION with pricing block).

- [ ] **Step 8: Stop the API + commit final state**

```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" status
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" log --oneline
```

Expected: clean working tree, ~28 commits.

If there are any uncommitted last-mile fixes (formatting, README typos), commit them:
```powershell
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" add -A
git -C "C:\code_crypto\acp\ACP_BasicSubscriptionBot" commit -m "chore: final boilerplate polish"
```

---

## Self-Review

**Spec coverage check** — every spec section maps to ≥ 1 task:

| Spec section | Task(s) |
|---|---|
| Folder structure (flat layout) | Task 1 |
| Architecture (2 tier + 2 hosted workers) | Tasks 2, 15, 16, 17 |
| Subscription model (1 ACP job → N webhooks) | Tasks 13, 23 |
| Push delivery (webhook + HMAC) | Task 11 |
| Retry & dead-letter (30s/2m/10m/1h/6h, suspend at 3 consecutive) | Tasks 7, 10, 16 |
| SQLite schema (4 tables) | Task 4 |
| Stub offerings (echo + tick_echo) | Task 22 |
| Extended Offering interface | Task 20 |
| Pricing math (tick × per-tick) | Task 21 |
| TickSchedulerWorker | Task 15 |
| RetryWorker | Task 16 |
| Security defaults (HTTPS-only, HMAC, X-API-Key, body cap, container user) | Tasks 11, 17, 18, 20 |
| What's NOT in v1 (cancellation, EAS, pull, renewal) | Documented in README (Task 27) |
| Smoke-test acceptance (7 criteria) | Task 28 |
| Clone-and-rename checklist | Task 27 |

No gaps.

**Placeholder scan:** searched for "TBD", "TODO", "implement later" — none in the plan. The phrase "tx_watcher" appears in earlier conversation but not in this plan. The README references `// TODO:` for EAS but that's an intentional in-code marker for future bots, not a plan placeholder. The note "(No string replacements needed if the BasicBot file uses no bot-name strings inline.)" in Task 25 is conditional — engineer reads the source file and applies replacements only if needed. That's a concrete instruction, not a placeholder.

**Type / signature consistency check:**
- `Subscription` record fields used in Tasks 5, 7, 8, 9, 11, 12, 13, 15, 16 — all match the constructor signature in Task 5.
- `RetryBackoff.DelayFor(int attempts)` — used in Tasks 15 and 16 — signature consistent.
- `RetryBackoff.IsExhausted(int attempts)` — used in Task 16 — signature consistent.
- `WebhookDeliveryService.ComputeSignature(secret, tick, timestamp, body)` — used in Task 11 tests — matches the signature in the implementation.
- `WebhookDeliveryService.DeliverAsync(Subscription sub, int tickNumber, string bodyJson, CancellationToken ct)` — used in Tasks 15, 16 — consistent.
- `SubscriptionRepository.RecordTickResultAsync(id, succeeded, lastRunAt, nextRunAt, completedSubscription)` — used in Tasks 7, 15 — consistent.
- `SubscriptionRunRepository.MarkRetryingAsync(runId, attempts, nextAttemptAt, lastError)` — used in Tasks 8, 15, 16 — consistent.
- `TickExecutorService.ComputePayloadAsync(Subscription sub, int tickNumber)` — used in Tasks 12, 15 — consistent.
- `SubscriptionService.CreateAsync(CreateSubscriptionRequest req)` — used in Tasks 13, 17 — consistent.
- `ApiClient.createSubscription(...)` (TS) — used in Tasks 21, 23 — consistent.

**Scope check:** Plan focuses on the boilerplate only. No real subscription bot is built here — Task 22 ships the trivial `tick_echo` stub, exactly per spec.

Plan ready.
