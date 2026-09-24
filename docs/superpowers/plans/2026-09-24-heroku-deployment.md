# Heroku Deployment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deploy MediQueue to Heroku (one Basic dyno + Heroku Postgres Essential-0) with Postgres verified in CI before it reaches the host.

**Architecture:** Each database provider gets a `DbContext` subclass that owns its migration set (SQLite for development, Postgres for production); configuration picks one and everything else keeps injecting `MediQueueDbContext`. Heroku's release phase runs the app with `--migrate-only` to migrate and seed before new dynos start; the web dyno trusts Heroku's forwarded headers so HTTPS is recognised. CI runs the API suite a second time against a real Postgres container.

**Tech Stack:** .NET 10, EF Core 10.0.12, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3, ASP.NET Core forwarded headers, Heroku .NET buildpack (GA June 2026), GitHub Actions service containers, GitHubActionsTestLogger 3.0.5.

**Spec:** `docs/superpowers/specs/2026-09-24-heroku-deployment-design.md`

---

## Facts established during planning

These were verified, not assumed; tasks depend on them.

- **The buildpack publishes whatever it is pointed at, with `--runtime linux-x64 -p:PublishDir=bin/publish`,** and its own web command is `cd <project>/bin/publish; ./<AssemblyName> --urls http://*:$PORT` (source: `buildpacks/dotnet/src/launch_process.rs`). It accepts a `.csproj` as its target via `project.toml` (`app_source.rs::from_file`).
- **Publishing the whole solution fails intermittently** on this repo: the Blazor client is built by several projects at once and they collide on `rpswa.dswa.cache.json`. Reproduced locally. Publishing `src/MediQueue.Api/MediQueue.Api.csproj` alone succeeds (exit 0, no warnings, 49 MB, apphost + `wwwroot/_framework` present).
- **Npgsql 10.0.3 requires EF Core ≥ 10.0.4.** The repo pins EF at 10.0.0, and a downgrade is an error under `TreatWarningsAsErrors`. All EF packages move to 10.0.12.
- **`ForwardedHeadersOptions.KnownNetworks` is obsolete in .NET 10;** use `KnownIPNetworks`.
- **GitHub Actions logs need a signed-in user even on public repos,** but check-run annotations are readable anonymously via the API. `GitHubActionsTestLogger` turns failed tests into annotations.
- **Two existing tests are flaky:** random two-digit department codes collide (≈10% of runs), and the analytics tests query "today" for tickets stamped three hours ago, so they fail when CI runs between 00:00 and ~03:40 UTC.
- **Disk:** under 1 GB free on the development machine. Publish artifacts cost ~250 MB and must be deleted immediately after use.

## Before you start

- [ ] **Create the branch and check disk**

```bash
git checkout -b feat/heroku-deploy
```

```powershell
Get-PSDrive C | Select-Object @{n='FreeGB';e={[math]::Round($_.Free/1GB,2)}}
```

Expected: branch created; at least 0.8 GB free. If less, stop and ask the user to free space: restore and publish need it.

## File structure

| File | Change | Responsibility |
| --- | --- | --- |
| `src/MediQueue.Infrastructure/MediQueue.Infrastructure.csproj` | Modify | EF 10.0.12, add Npgsql, drop SQL Server |
| `src/MediQueue.Infrastructure/Persistence/PostgresConnectionString.cs` | Create | Heroku `DATABASE_URL` → Npgsql connection string |
| `src/MediQueue.Infrastructure/Persistence/MediQueueDbContext.cs` | Modify | Becomes abstract; shared model |
| `src/MediQueue.Infrastructure/Persistence/SqliteMediQueueDbContext.cs` | Create | Owns SQLite migrations |
| `src/MediQueue.Infrastructure/Persistence/PostgresMediQueueDbContext.cs` | Create | Owns Postgres migrations |
| `src/MediQueue.Infrastructure/Persistence/DesignTimeDbContextFactory.cs` | Delete | Replaced by the file below |
| `src/MediQueue.Infrastructure/Persistence/DesignTimeDbContextFactories.cs` | Create | One `dotnet ef` factory per provider |
| `src/MediQueue.Infrastructure/Persistence/Migrations/Sqlite/*` | Move | Existing migration, same ID |
| `src/MediQueue.Infrastructure/Persistence/Migrations/Postgres/*` | Generate | Postgres `InitialSchema` |
| `src/MediQueue.Infrastructure/DependencyInjection.cs` | Modify | Provider switch; fail on unknown provider |
| `src/MediQueue.Infrastructure/Seed/DemoSeedPassword.cs` | Create | Decide and validate the seeded staff password |
| `src/MediQueue.Infrastructure/Seed/DemoDataSeeder.cs` | Modify | Take the password; fail instead of skipping staff |
| `src/MediQueue.Api/DatabaseStartup.cs` | Create | Migrate, then seed where enabled |
| `src/MediQueue.Api/Program.cs` | Modify | `--migrate-only`, forwarded headers, use `DatabaseStartup` |
| `tests/MediQueue.Api.Tests/MediQueueApiFactory.cs` | Modify | Provider chosen through configuration; Postgres mode |
| `tests/MediQueue.Api.Tests/Persistence/*.cs` | Create | Connection string, provider switch, seed password tests |
| `tests/MediQueue.Api.Tests/ProviderSelectionTests.cs` | Create | Guards that the suite really runs on the chosen provider |
| `tests/MediQueue.Api.Tests/ForwardedHeadersTests.cs` | Create | HTTPS behind the router |
| `tests/*/…Tests.csproj` | Modify | Add `GitHubActionsTestLogger` |
| `.github/workflows/ci.yml` | Modify | All branches; migration drift; Postgres job |
| `Procfile`, `project.toml`, `.gitattributes`, `.editorconfig` | Create/Modify | Heroku process types, build target, LF endings |
| `docker-compose.yml` | Modify | SQL Server → Postgres |
| `README.md` | Modify | Heroku runbook, configuration, remove SQL Server claims |

---

### Task 1: Stop two flaky tests from blocking deploys

Once Heroku waits for CI, a flaky test blocks a deploy. Fix both before CI becomes a gate.

**Files:**
- Modify: `tests/MediQueue.Api.Tests/MediQueueApiFactory.cs`
- Modify: `tests/MediQueue.Api.Tests/AuthorizationTests.cs`
- Modify: `tests/MediQueue.Api.Tests/CheckInEndpointTests.cs`
- Modify: `tests/MediQueue.Api.Tests/AnalyticsTests.cs`

- [ ] **Step 1: Add a unique department code generator to the factory**

In `MediQueueApiFactory.cs`, directly after `public const string Password = "TestPass#2026";`, add:

```csharp
    private static int _departmentSequence;

    /// <summary>
    /// A department code no other test in the run has used. Codes carry a
    /// unique index, and the random two-digit codes used before collided often
    /// enough to fail roughly one CI run in ten.
    /// </summary>
    public static string UniqueDepartmentCode() =>
        $"T{Interlocked.Increment(ref _departmentSequence):D4}";
```

- [ ] **Step 2: Use it where codes were random**

In `AuthorizationTests.cs` replace:

```csharp
        var department = await _factory.AddDepartmentAsync(code: $"Q{Random.Shared.Next(10, 99)}");
```

with:

```csharp
        var department = await _factory.AddDepartmentAsync(code: MediQueueApiFactory.UniqueDepartmentCode());
```

In `CheckInEndpointTests.cs` replace:

```csharp
        var department = await _factory.AddDepartmentAsync(code: $"R{Random.Shared.Next(10, 99)}");
```

with:

```csharp
        var department = await _factory.AddDepartmentAsync(code: MediQueueApiFactory.UniqueDepartmentCode());
```

- [ ] **Step 3: Widen the analytics window so it survives midnight**

In `AnalyticsTests.cs`, the tickets are stamped three hours before "now", so between midnight and ~03:40 UTC they fall on yesterday. Replace both occurrences (use replace-all) of:

```csharp
            $"/api/analytics/summary?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
```

with:

```csharp
            // From yesterday: tickets are stamped hours ago, which is yesterday
            // for a run shortly after midnight UTC.
            $"/api/analytics/summary?from={today.AddDays(-1):yyyy-MM-dd}&to={today:yyyy-MM-dd}");
```

Each test's assertions are scoped to its own ward (`Analytics Ward`, `No Show Ward`), so the wider range cannot pull in other tickets.

- [ ] **Step 4: Run the API suite**

Run: `dotnet test tests/MediQueue.Api.Tests`
Expected: `Passed!  - Failed: 0, Passed: 37`

- [ ] **Step 5: Commit**

```bash
git add tests/MediQueue.Api.Tests
git commit -m "Remove two sources of flaky API test failures" -m "Random two-digit department codes collided on a unique index in roughly one run in ten, and the analytics tests missed their own tickets when run shortly after midnight UTC. Once Heroku waits for CI, either would block a deploy."
```

---

### Task 2: Move EF Core to 10.0.12 and add the Postgres provider

**Files:**
- Modify: `src/MediQueue.Infrastructure/MediQueue.Infrastructure.csproj`

- [ ] **Step 1: Update package references**

Replace the first package `ItemGroup` in `MediQueue.Infrastructure.csproj` with:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.12" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.12" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.12" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.12" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.5" />
  </ItemGroup>
```

SQL Server stays for one more task, because `DependencyInjection.cs` still calls `UseSqlServer`; Task 4 removes both together.

- [ ] **Step 2: Restore and check for advisories or downgrades**

Run: `dotnet restore MediQueue.slnx`
Expected: `Restored …` for each project and no `NU1903` (vulnerability) or `NU1605` (downgrade) errors. If an `NU1903` names a transitive package, pin it forward in the second `ItemGroup` next to `System.Security.Cryptography.Xml`, the same way that one is pinned, and restore again.

- [ ] **Step 3: Build and test**

Run: `dotnet build MediQueue.slnx` → `0 Warning(s)  0 Error(s)`
Run: `dotnet test MediQueue.slnx` → 53 + 27 + 37 passed.

- [ ] **Step 4: Update the EF CLI to match**

```bash
dotnet tool update --global dotnet-ef --version 10.0.12
```

Expected: `Tool 'dotnet-ef' was successfully updated … to version '10.0.12'`. (Tools live in `%USERPROFILE%\.dotnet\tools`; prepend it to `PATH` in PowerShell calls if `dotnet ef` is not found.)

- [ ] **Step 5: Commit**

```bash
git add src/MediQueue.Infrastructure/MediQueue.Infrastructure.csproj
git commit -m "Move EF Core to 10.0.12 and add the Npgsql provider" -m "Npgsql 10.0.3 requires EF Core 10.0.4 or later, and a package downgrade fails the build under warnings-as-errors, so every EF package moves together."
```

---

### Task 3: Translate Heroku's DATABASE_URL

**Files:**
- Create: `src/MediQueue.Infrastructure/Persistence/PostgresConnectionString.cs`
- Test: `tests/MediQueue.Api.Tests/Persistence/PostgresConnectionStringTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/MediQueue.Api.Tests/Persistence/PostgresConnectionStringTests.cs`:

```csharp
using MediQueue.Infrastructure.Persistence;
using Npgsql;

namespace MediQueue.Api.Tests.Persistence;

public class PostgresConnectionStringTests
{
    private static NpgsqlConnectionStringBuilder Parse(string url) =>
        new(PostgresConnectionString.FromUrl(url));

    [Fact]
    public void Every_part_of_a_heroku_url_is_carried_across()
    {
        var result = Parse("postgres://u9x:s3cret@ec2-1-2-3-4.compute-1.amazonaws.com:5433/d7abc");

        Assert.Equal("ec2-1-2-3-4.compute-1.amazonaws.com", result.Host);
        Assert.Equal(5433, result.Port);
        Assert.Equal("d7abc", result.Database);
        Assert.Equal("u9x", result.Username);
        Assert.Equal("s3cret", result.Password);
    }

    [Fact]
    public void The_postgresql_scheme_is_accepted_too()
    {
        Assert.Equal("db", Parse("postgresql://u:p@host:5432/db").Database);
    }

    [Fact]
    public void A_missing_port_means_the_postgres_default()
    {
        Assert.Equal(5432, Parse("postgres://u:p@host/db").Port);
    }

    [Fact]
    public void Escaped_characters_in_credentials_are_decoded()
    {
        var result = Parse("postgres://user%40ops:p%3Ass%2Fword@host:5432/db");

        Assert.Equal("user@ops", result.Username);
        Assert.Equal("p:ss/word", result.Password);
    }

    [Fact]
    public void Connections_are_encrypted_because_heroku_refuses_plain_ones()
    {
        Assert.Equal(SslMode.Require, Parse("postgres://u:p@host:5432/db").SslMode);
    }

    [Fact]
    public void The_pool_stays_well_inside_the_plans_connection_limit()
    {
        Assert.Equal(10, Parse("postgres://u:p@host:5432/db").MaxPoolSize);
    }

    [Theory]
    [InlineData("mysql://u:p@host:3306/db")]
    [InlineData("not a url")]
    [InlineData("")]
    public void Anything_that_is_not_a_postgres_url_is_refused(string url)
    {
        Assert.Throws<ArgumentException>(() => PostgresConnectionString.FromUrl(url));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/MediQueue.Api.Tests --filter "FullyQualifiedName~PostgresConnectionStringTests"`
Expected: build error `CS0103: The name 'PostgresConnectionString' does not exist`.

- [ ] **Step 3: Implement**

Create `src/MediQueue.Infrastructure/Persistence/PostgresConnectionString.cs`:

```csharp
using Npgsql;

namespace MediQueue.Infrastructure.Persistence;

/// <summary>
/// Heroku hands the app its database as a URL
/// (<c>postgres://user:password@host:port/database</c>); Npgsql wants
/// key-value pairs. This is the translation, plus the two settings Heroku
/// Postgres needs that the URL does not carry.
/// </summary>
public static class PostgresConnectionString
{
    /// <summary>
    /// Essential-0 allows 20 connections. Ten leaves room for the release
    /// phase and a <c>heroku pg:psql</c> session while the web dyno is busy.
    /// </summary>
    public const int MaxPoolSize = 10;

    public static string FromUrl(string databaseUrl)
    {
        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            throw new ArgumentException(
                "DATABASE_URL must look like postgres://user:password@host:port/database.",
                nameof(databaseUrl));
        }

        var credentials = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : null,

            // Heroku Postgres refuses unencrypted connections. Its certificates
            // are not issued by a public CA, and Require encrypts without
            // validating the chain.
            SslMode = SslMode.Require,
            MaxPoolSize = MaxPoolSize
        };

        return builder.ConnectionString;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/MediQueue.Api.Tests --filter "FullyQualifiedName~PostgresConnectionStringTests"`
Expected: `Passed: 9, Failed: 0`

- [ ] **Step 5: Commit**

```bash
git add src/MediQueue.Infrastructure/Persistence/PostgresConnectionString.cs tests/MediQueue.Api.Tests/Persistence
git commit -m "Translate Heroku's DATABASE_URL into an Npgsql connection string" -m "Heroku supplies the database as a URL and requires SSL. The pool is capped at 10 so Essential-0's 20-connection limit leaves room for the release phase."
```

---

### Task 4: One context per provider, chosen by configuration

**Files:**
- Modify: `src/MediQueue.Infrastructure/Persistence/MediQueueDbContext.cs`
- Create: `src/MediQueue.Infrastructure/Persistence/SqliteMediQueueDbContext.cs`
- Create: `src/MediQueue.Infrastructure/Persistence/PostgresMediQueueDbContext.cs`
- Delete: `src/MediQueue.Infrastructure/Persistence/DesignTimeDbContextFactory.cs`
- Create: `src/MediQueue.Infrastructure/Persistence/DesignTimeDbContextFactories.cs`
- Move: `src/MediQueue.Infrastructure/Persistence/Migrations/*.cs` → `Migrations/Sqlite/`
- Modify: `src/MediQueue.Infrastructure/DependencyInjection.cs`
- Modify: `src/MediQueue.Infrastructure/MediQueue.Infrastructure.csproj`
- Modify: `tests/MediQueue.Api.Tests/MediQueueApiFactory.cs`
- Test: `tests/MediQueue.Api.Tests/Persistence/DatabaseProviderTests.cs`
- Test: `tests/MediQueue.Api.Tests/ProviderSelectionTests.cs`

- [ ] **Step 1: Write the failing provider-switch tests**

Create `tests/MediQueue.Api.Tests/Persistence/DatabaseProviderTests.cs`:

```csharp
using MediQueue.Infrastructure;
using MediQueue.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediQueue.Api.Tests.Persistence;

public class DatabaseProviderTests
{
    private static IServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ServiceCollection()
            .AddLogging()
            .AddMediQueueInfrastructure(configuration)
            .BuildServiceProvider();
    }

    private static MediQueueDbContext Resolve(Dictionary<string, string?> settings) =>
        Build(settings).CreateScope().ServiceProvider.GetRequiredService<MediQueueDbContext>();

    [Fact]
    public void Sqlite_is_the_default_so_a_fresh_clone_runs_with_no_setup()
    {
        var db = Resolve(new());

        Assert.IsType<SqliteMediQueueDbContext>(db);
        Assert.Contains("mediqueue.db", db.Database.GetConnectionString());
    }

    [Fact]
    public void Postgres_is_selected_by_configuration()
    {
        var db = Resolve(new()
        {
            ["Database:Provider"] = "Postgres",
            ["ConnectionStrings:Default"] = "Host=db;Database=mediqueue"
        });

        Assert.IsType<PostgresMediQueueDbContext>(db);
    }

    [Fact]
    public void On_heroku_DATABASE_URL_wins_over_the_default_connection_string()
    {
        var db = Resolve(new()
        {
            ["Database:Provider"] = "Postgres",
            ["DATABASE_URL"] = "postgres://u:p@heroku-host:5432/herokudb",
            ["ConnectionStrings:Default"] = "Host=elsewhere;Database=other"
        });

        Assert.Contains("heroku-host", db.Database.GetConnectionString());
    }

    [Fact]
    public void Postgres_without_any_connection_details_stops_startup()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Build(new() { ["Database:Provider"] = "Postgres" }));

        Assert.Contains("DATABASE_URL", error.Message);
    }

    [Fact]
    public void A_misspelt_provider_stops_startup_instead_of_quietly_using_sqlite()
    {
        // On Heroku a silent fallback to SQLite would put the data on a disk
        // that is wiped at least once a day.
        var error = Assert.Throws<InvalidOperationException>(
            () => Build(new() { ["Database:Provider"] = "Postgress" }));

        Assert.Contains("Postgress", error.Message);
    }
}
```

Create `tests/MediQueue.Api.Tests/ProviderSelectionTests.cs`:

```csharp
using MediQueue.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediQueue.Api.Tests;

/// <summary>
/// Guards the suite itself. If the factory's settings ever stopped reaching
/// the app, the Postgres run in CI would quietly test SQLite and prove nothing.
/// </summary>
public class ProviderSelectionTests : IClassFixture<MediQueueApiFactory>
{
    private readonly MediQueueApiFactory _factory;

    public ProviderSelectionTests(MediQueueApiFactory factory) => _factory = factory;

    [Fact]
    public void The_app_under_test_uses_the_provider_the_run_asked_for()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediQueueDbContext>();

        var expected = _factory.UsesPostgres
            ? typeof(PostgresMediQueueDbContext)
            : typeof(SqliteMediQueueDbContext);

        Assert.IsType(expected, db);
    }

    [Fact]
    public void The_app_under_test_uses_a_throwaway_database_not_the_development_file()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediQueueDbContext>();

        Assert.Contains("mediqueue_test_", db.Database.GetConnectionString());
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/MediQueue.Api.Tests --filter "FullyQualifiedName~DatabaseProviderTests|FullyQualifiedName~ProviderSelectionTests"`
Expected: build errors — `SqliteMediQueueDbContext`, `PostgresMediQueueDbContext` and `UsesPostgres` do not exist.

- [ ] **Step 3: Make the shared context abstract**

In `MediQueueDbContext.cs`, replace:

```csharp
public class MediQueueDbContext : IdentityDbContext<ApplicationUser>
{
    public MediQueueDbContext(DbContextOptions<MediQueueDbContext> options) : base(options)
    {
    }
```

with:

```csharp
/// <summary>
/// The model, shared by every provider. Abstract because each provider has a
/// subclass that owns that provider's migrations; resolving this type from DI
/// yields whichever one configuration selected.
/// </summary>
public abstract class MediQueueDbContext : IdentityDbContext<ApplicationUser>
{
    protected MediQueueDbContext(DbContextOptions options) : base(options)
    {
    }
```

And in the same file replace the last sentence of the `ApplySqliteTimestampConversion` summary:

```csharp
    /// database rather than being pulled into memory. SQL Server keeps its
    /// native <c>datetimeoffset</c> and never sees this.
```

with:

```csharp
    /// database rather than being pulled into memory. Postgres stores
    /// <c>timestamp with time zone</c> natively and never sees this.
```

- [ ] **Step 4: Add the two provider contexts**

Create `src/MediQueue.Infrastructure/Persistence/SqliteMediQueueDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace MediQueue.Infrastructure.Persistence;

/// <summary>
/// The development database. A separate type only so that SQLite's
/// migrations, which use SQLite-specific column types, are found for SQLite
/// and never applied anywhere else.
/// </summary>
public sealed class SqliteMediQueueDbContext(DbContextOptions<SqliteMediQueueDbContext> options)
    : MediQueueDbContext(options);
```

Create `src/MediQueue.Infrastructure/Persistence/PostgresMediQueueDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace MediQueue.Infrastructure.Persistence;

/// <summary>
/// The production database on Heroku. A separate type only so that it owns
/// migrations generated against Postgres.
/// </summary>
public sealed class PostgresMediQueueDbContext(DbContextOptions<PostgresMediQueueDbContext> options)
    : MediQueueDbContext(options);
```

- [ ] **Step 5: Replace the design-time factory**

```bash
git rm src/MediQueue.Infrastructure/Persistence/DesignTimeDbContextFactory.cs
```

Create `src/MediQueue.Infrastructure/Persistence/DesignTimeDbContextFactories.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MediQueue.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>; pass <c>--context</c> to choose which
/// provider's migrations to work on. Neither connects to a server: generating
/// and checking migrations needs only the provider.
/// </summary>
public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteMediQueueDbContext>
{
    public SqliteMediQueueDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteMediQueueDbContext>()
            .UseSqlite("Data Source=mediqueue-design.db")
            .Options);
}

public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresMediQueueDbContext>
{
    public PostgresMediQueueDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresMediQueueDbContext>()
            .UseNpgsql("Host=localhost;Database=mediqueue_design")
            .Options);
}
```

- [ ] **Step 6: Move the SQLite migration under its context, keeping its ID**

The migration ID (`20260814224156_InitialSchema`) must not change: existing local databases record it in `__EFMigrationsHistory`, and a new ID would make EF try to create tables that already exist.

```bash
cd src/MediQueue.Infrastructure/Persistence/Migrations
mkdir Sqlite
git mv 20260814224156_InitialSchema.cs Sqlite/
git mv 20260814224156_InitialSchema.Designer.cs Sqlite/
git mv MediQueueDbContextModelSnapshot.cs Sqlite/SqliteMediQueueDbContextModelSnapshot.cs
# \b, not $: the generated files are CRLF, and $ would not match before the \r.
sed -i 's/^namespace MediQueue\.Infrastructure\.Persistence\.Migrations\b/namespace MediQueue.Infrastructure.Persistence.Migrations.Sqlite/' Sqlite/*.cs
sed -i 's/\[DbContext(typeof(MediQueueDbContext))\]/[DbContext(typeof(SqliteMediQueueDbContext))]/' Sqlite/*.cs
sed -i 's/partial class MediQueueDbContextModelSnapshot/partial class SqliteMediQueueDbContextModelSnapshot/' Sqlite/SqliteMediQueueDbContextModelSnapshot.cs
grep -n "^namespace\|DbContext(typeof\|partial class\|\[Migration(" Sqlite/*.cs
cd -
```

Expected grep output: all three files in namespace `MediQueue.Infrastructure.Persistence.Migrations.Sqlite`, two `[DbContext(typeof(SqliteMediQueueDbContext))]`, `[Migration("20260814224156_InitialSchema")]` unchanged, and `partial class SqliteMediQueueDbContextModelSnapshot`.

- [ ] **Step 7: Select the provider in DI and drop SQL Server**

Replace the whole of `src/MediQueue.Infrastructure/DependencyInjection.cs` with:

```csharp
using MediQueue.Domain.Abstractions;
using MediQueue.Domain.Queues;
using MediQueue.Infrastructure.Identity;
using MediQueue.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediQueue.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence and identity. <c>Database:Provider</c> chooses
    /// the database: <c>Sqlite</c> by default, so a fresh clone runs with no
    /// setup, or <c>Postgres</c> in deployment.
    /// </summary>
    public static IServiceCollection AddMediQueueInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        AddDatabase(services, configuration);

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireDigit = true;

                options.User.RequireUniqueEmail = true;

                // A shared front-desk terminal invites shoulder-surfing, so a
                // wrong password is expensive rather than merely annoying.
                // Lockout is applied by the auth service via UserManager, since
                // this library has no ASP.NET Core framework reference.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<MediQueueDbContext>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<QueueEngine>();

        return services;
    }

    /// <summary>
    /// Registers the context for the configured provider. Each provider has
    /// its own subclass because each owns its own migrations; everything else
    /// asks for <see cref="MediQueueDbContext"/> and never knows which it got.
    /// </summary>
    private static void AddDatabase(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "Sqlite";

        if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetConnectionString("Default")
                ?? "Data Source=mediqueue.db";

            services.AddDbContext<MediQueueDbContext, SqliteMediQueueDbContext>(
                options => options.UseSqlite(connectionString));
            return;
        }

        if (string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            // Heroku supplies DATABASE_URL; anywhere else, a normal connection string.
            var databaseUrl = configuration["DATABASE_URL"];
            var connectionString = !string.IsNullOrWhiteSpace(databaseUrl)
                ? PostgresConnectionString.FromUrl(databaseUrl)
                : configuration.GetConnectionString("Default")
                  ?? throw new InvalidOperationException(
                      "Database:Provider is Postgres, but neither DATABASE_URL nor ConnectionStrings:Default is set.");

            services.AddDbContext<MediQueueDbContext, PostgresMediQueueDbContext>(
                options => options.UseNpgsql(connectionString));
            return;
        }

        // An unknown value used to fall back to SQLite silently. On Heroku that
        // would mean a database on a disk that is wiped at least once a day.
        throw new InvalidOperationException(
            $"Unknown Database:Provider '{provider}'. Use 'Sqlite' or 'Postgres'.");
    }
}
```

In `MediQueue.Infrastructure.csproj`, delete this line:

```xml
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.12" />
```

- [ ] **Step 8: Drive the test factory through configuration**

Replace `tests/MediQueue.Api.Tests/MediQueueApiFactory.cs` with:

```csharp
using MediQueue.Domain.Entities;
using MediQueue.Infrastructure.Identity;
using MediQueue.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediQueue.Api.Tests;

/// <summary>
/// Hosts the real pipeline — routing, authorization, SignalR — against a
/// throwaway database. The provider is chosen through configuration exactly as
/// in production, so the suite exercises the real provider switch: in-memory
/// SQLite by default, or Postgres when <see cref="PostgresVariable"/> is set,
/// as it is in CI.
/// </summary>
public class MediQueueApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "TestPass#2026";

    /// <summary>
    /// A Postgres server connection string without a database name. Each
    /// factory creates, and afterwards drops, its own database on that server.
    /// </summary>
    public const string PostgresVariable = "MEDIQUEUE_TEST_POSTGRES";

    private static int _departmentSequence;

    private readonly string _databaseName = $"mediqueue_test_{Guid.NewGuid():N}";

    /// <summary>
    /// A shared in-memory SQLite database lives only while a connection to it
    /// is open, so the factory holds one for its whole lifetime.
    /// </summary>
    private SqliteConnection? _keepAlive;

    private static string? PostgresServer => Environment.GetEnvironmentVariable(PostgresVariable);

    public bool UsesPostgres => !string.IsNullOrWhiteSpace(PostgresServer);

    private string ConnectionString => UsesPostgres
        ? $"{PostgresServer};Database={_databaseName}"
        : $"Data Source={_databaseName};Mode=Memory;Cache=Shared";

    /// <summary>
    /// A department code no other test in the run has used. Codes carry a
    /// unique index, and the random two-digit codes used before collided often
    /// enough to fail roughly one CI run in ten.
    /// </summary>
    public static string UniqueDepartmentCode() =>
        $"T{Interlocked.Increment(ref _departmentSequence):D4}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("Database:Provider", UsesPostgres ? "Postgres" : "Sqlite");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);

        // Blanked so a DATABASE_URL on the machine can never redirect the suite.
        builder.UseSetting("DATABASE_URL", string.Empty);

        builder.UseSetting("Seed:DemoData", "false");
        builder.UseSetting("Jwt:Key", "test-signing-key-that-is-long-enough-for-hmac-sha256");

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSimpleConsole(o => o.SingleLine = false);
            logging.SetMinimumLevel(LogLevel.Warning);
        });
    }

    public async Task InitializeAsync()
    {
        if (!UsesPostgres)
        {
            _keepAlive = new SqliteConnection(ConnectionString);
            await _keepAlive.OpenAsync();
        }

        // Starting the host runs the app's own startup migration; this makes
        // the dependency explicit rather than relying on it.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediQueueDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        if (UsesPostgres)
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MediQueueDbContext>().Database.EnsureDeletedAsync();
        }

        if (_keepAlive is not null)
        {
            await _keepAlive.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    public async Task<Department> AddDepartmentAsync(
        string name = "Cardiology",
        string code = "CAR",
        int rooms = 2,
        int defaultServiceMinutes = 15)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediQueueDbContext>();

        var department = new Department
        {
            Name = name,
            Code = code,
            ConsultationRooms = rooms,
            DefaultServiceMinutes = defaultServiceMinutes
        };

        db.Departments.Add(department);
        await db.SaveChangesAsync();

        return department;
    }

    public async Task<ApplicationUser> AddStaffAsync(string role, string? email = null, int? departmentId = null)
    {
        using var scope = Services.CreateScope();
        var sp = scope.ServiceProvider;

        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(role))
        {
            await roles.CreateAsync(new IdentityRole(role));
        }

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email ??= $"{role.ToLowerInvariant()}@mediqueue.test",
            Email = email,
            EmailConfirmed = true,
            FullName = $"Test {role}",
            DepartmentId = departmentId
        };

        var result = await users.CreateAsync(user, Password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));

        await users.AddToRoleAsync(user, role);
        return user;
    }

    /// <summary>A client already carrying a bearer token for the given role.</summary>
    public async Task<HttpClient> CreateClientAsAsync(string role, int? departmentId = null)
    {
        var user = await AddStaffAsync(role, $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@mediqueue.test", departmentId);

        var client = CreateClient();
        var response = await client.PostJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = user.Email!,
            Password = Password
        });

        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadJsonAsync<LoginResponse>();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", login!.AccessToken);

        return client;
    }

    public async Task<T> UseDbAsync<T>(Func<MediQueueDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediQueueDbContext>();
        return await work(db);
    }
}
```

(This also repairs the mojibake — `â€”` — that an earlier re-encode left in the class summary.)

- [ ] **Step 9: Run the whole suite**

Run: `dotnet build MediQueue.slnx` → `0 Warning(s)  0 Error(s)`
Run: `dotnet test MediQueue.slnx`
Expected: Domain 53, Client 27, API 37 + 9 + 5 + 2 = 53 passed. In particular `The_app_under_test_uses_a_throwaway_database_not_the_development_file` passes, which proves the factory's settings reach the app's configuration before services are registered.

- [ ] **Step 10: Verify the SQLite migration is intact and nothing drifted**

```powershell
$env:Path = "$env:USERPROFILE\.dotnet\tools;$env:Path"
dotnet ef migrations list --project src/MediQueue.Infrastructure --context SqliteMediQueueDbContext
dotnet ef migrations has-pending-model-changes --project src/MediQueue.Infrastructure --context SqliteMediQueueDbContext
```

Expected: `20260814224156_InitialSchema`, then `No changes have been made to the model since the last migration.`

- [ ] **Step 11: Verify an existing local database still starts**

Start the dev server (preview `mediqueue` config) and read its log.
Expected: `No migrations were applied. The database is already up to date.` and `Database already seeded; leaving it alone.` Stop the server afterwards (it locks DLLs).

- [ ] **Step 12: Commit**

```bash
git add -A src/MediQueue.Infrastructure tests/MediQueue.Api.Tests
git commit -m "Give each database provider its own context and migrations" -m "The existing migrations are SQLite-specific, so they now belong to a SQLite context and keep their ID, which leaves existing local databases untouched. Configuration selects the provider; an unknown value now stops startup instead of silently falling back to SQLite. SQL Server is removed." -m "The test factory now picks its provider through configuration, the same path production uses, and a guard test fails if those settings ever stop reaching the app."
```

---

### Task 5: Generate the Postgres migration

**Files:**
- Create: `src/MediQueue.Infrastructure/Persistence/Migrations/Postgres/*` (generated)

- [ ] **Step 1: Generate**

```powershell
$env:Path = "$env:USERPROFILE\.dotnet\tools;$env:Path"
dotnet ef migrations add InitialSchema --project src/MediQueue.Infrastructure --context PostgresMediQueueDbContext --output-dir Persistence/Migrations/Postgres --namespace MediQueue.Infrastructure.Persistence.Migrations.Postgres
```

Expected: `Done.` and three files under `Migrations/Postgres/`.

- [ ] **Step 2: Check it is really Postgres-shaped**

```bash
cd src/MediQueue.Infrastructure/Persistence/Migrations/Postgres
grep -c "Sqlite:" *.cs
grep -oE 'type: "[a-z ]+(\([0-9]+\))?"' *_InitialSchema.cs | sort | uniq -c
cd -
```

Expected: zero `Sqlite:` annotations in every file; column types such as `uuid`, `integer`, `bigint`, `boolean`, `text`, `character varying(…)`, `timestamp with time zone` and `date`. (`text` is an ordinary, indexable type in Postgres; the SQL Server failure came from SQLite's `TEXT` being emitted verbatim.)

- [ ] **Step 3: Confirm no drift for either provider**

```powershell
$env:Path = "$env:USERPROFILE\.dotnet\tools;$env:Path"
dotnet ef migrations has-pending-model-changes --project src/MediQueue.Infrastructure --context PostgresMediQueueDbContext
dotnet ef migrations has-pending-model-changes --project src/MediQueue.Infrastructure --context SqliteMediQueueDbContext
```

Expected: `No changes have been made to the model since the last migration.` twice. The migration cannot be applied locally (no Postgres); Task 7 applies it in CI.

- [ ] **Step 4: Build, test, commit**

Run: `dotnet build MediQueue.slnx` → `0 Warning(s)  0 Error(s)`; `dotnet test MediQueue.slnx` → all pass.

```bash
git add src/MediQueue.Infrastructure/Persistence/Migrations/Postgres
git commit -m "Add the Postgres initial migration" -m "Generated against Npgsql, so it carries Postgres types (uuid, timestamp with time zone) rather than the SQLite-specific ones that made the old SQL Server path fail."
```

---

### Task 6: Run the API suite against real Postgres in CI

**Files:**
- Modify: `tests/MediQueue.Api.Tests/MediQueue.Api.Tests.csproj`
- Modify: `tests/MediQueue.Domain.Tests/MediQueue.Domain.Tests.csproj`
- Modify: `tests/MediQueue.Client.Tests/MediQueue.Client.Tests.csproj`
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Make test failures readable without signing in to GitHub**

```bash
dotnet add tests/MediQueue.Api.Tests package GitHubActionsTestLogger --version 3.0.5
dotnet add tests/MediQueue.Domain.Tests package GitHubActionsTestLogger --version 3.0.5
dotnet add tests/MediQueue.Client.Tests package GitHubActionsTestLogger --version 3.0.5
```

Expected: `PackageReference for package 'GitHubActionsTestLogger' version '3.0.5' added` three times, with no `NU1903`.

- [ ] **Step 2: Replace the workflow**

Replace `.github/workflows/ci.yml` with:

```yaml
name: CI

on:
  # Every branch, so a change is proven before it reaches main, where Heroku
  # deploys only after these checks pass.
  push:
    branches: ['**']
  pull_request:
    branches: [main]

jobs:
  build:
    name: Build and test (SQLite)
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore MediQueue.slnx

      - name: Build
        run: dotnet build MediQueue.slnx --configuration Release --no-restore

      - name: Test
        run: dotnet test MediQueue.slnx --configuration Release --no-build --logger GitHubActions

      # GITHUB_PATH takes effect from the next step, hence two steps.
      - name: Install EF tools
        run: |
          dotnet tool install --global dotnet-ef --version 10.0.12
          echo "$HOME/.dotnet/tools" >> "$GITHUB_PATH"

      # A model change without a matching migration would fail on deploy, not here.
      - name: Check migrations match the model
        run: |
          dotnet ef migrations has-pending-model-changes --project src/MediQueue.Infrastructure --context SqliteMediQueueDbContext --configuration Release --no-build
          dotnet ef migrations has-pending-model-changes --project src/MediQueue.Infrastructure --context PostgresMediQueueDbContext --configuration Release --no-build

  postgres:
    name: API tests (Postgres)
    runs-on: ubuntu-latest

    # Production runs on Postgres, and no developer machine is required to have
    # it, so this job is where the Postgres migration and queries are proven.
    services:
      postgres:
        image: postgres:17
        env:
          POSTGRES_USER: mediqueue
          POSTGRES_PASSWORD: mediqueue
        ports:
          - 5432:5432
        options: >-
          --health-cmd "pg_isready -U mediqueue"
          --health-interval 5s
          --health-timeout 5s
          --health-retries 12

    env:
      MEDIQUEUE_TEST_POSTGRES: Host=localhost;Port=5432;Username=mediqueue;Password=mediqueue

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Test the API against Postgres
        run: dotnet test tests/MediQueue.Api.Tests --configuration Release --logger GitHubActions
```

The Postgres credentials are for a throwaway container that exists only for the job.

- [ ] **Step 3: Verify locally that nothing broke**

Run: `dotnet test MediQueue.slnx` → all pass (the logger is inert outside GitHub Actions).

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci.yml tests
git commit -m "Run the API suite against Postgres in CI" -m "Production is Postgres and no development machine needs it installed, so CI is where the Postgres migration and queries are proven. CI now also fails if a model change lacks a migration, and reports failed tests as annotations, which are readable without signing in to GitHub."
```

---

### Task 7: Prove Postgres in CI (checkpoint)

Nothing below is worth doing if the Postgres job fails. Stop here until it is green.

- [ ] **Step 1: Push the branch**

```bash
git push -u origin feat/heroku-deploy
```

- [ ] **Step 2: Wait for the run to finish**

Poll at most once a minute (anonymous API limit is 60 requests an hour), in the background, until `status` is `completed`:

```powershell
$repo = "https://api.github.com/repos/emmanuelotoo/MediQueue"
$sha = (git rev-parse HEAD)
$run = (Invoke-RestMethod "$repo/actions/runs?branch=feat/heroku-deploy&per_page=5").workflow_runs | Where-Object head_sha -eq $sha | Select-Object -First 1
"$($run.status) $($run.conclusion)"
```

- [ ] **Step 3: Read the result per job**

```powershell
$jobs = (Invoke-RestMethod "$repo/actions/runs/$($run.id)/jobs").jobs
$jobs | ForEach-Object { "$($_.name): $($_.conclusion)" }
$jobs | Where-Object conclusion -eq 'failure' | ForEach-Object {
    Invoke-RestMethod "$repo/check-runs/$($_.id)/annotations" | ForEach-Object { "$($_.path):$($_.start_line)  $($_.message)" }
}
```

Expected: `Build and test (SQLite): success` and `API tests (Postgres): success`.

- [ ] **Step 4: If the Postgres job fails**

Use superpowers:systematic-debugging on the annotations. The likeliest causes, in order:

1. **A `DateTimeOffset` with a non-zero offset** reaching `timestamp with time zone` (Npgsql rejects it). Find the value's source and make it UTC at the source; do not add a converter.
2. **A query EF translates for SQLite but not Postgres.** Rewrite the query; do not move it into memory.
3. **`EnsureDeletedAsync` failing in fixture cleanup.** Cleanup failure is not a product bug; confirm by reading the message before changing anything.

Fix, run `dotnet test MediQueue.slnx` locally, commit, push, repeat from Step 2.

---

### Task 8: Refuse to seed with a missing, weak or public password

**Files:**
- Create: `src/MediQueue.Infrastructure/Seed/DemoSeedPassword.cs`
- Modify: `src/MediQueue.Infrastructure/Seed/DemoDataSeeder.cs`
- Create: `src/MediQueue.Api/DatabaseStartup.cs`
- Modify: `src/MediQueue.Api/Program.cs`
- Test: `tests/MediQueue.Api.Tests/Persistence/DemoSeedPasswordTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/MediQueue.Api.Tests/Persistence/DemoSeedPasswordTests.cs`:

```csharp
using MediQueue.Infrastructure.Identity;
using MediQueue.Infrastructure.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediQueue.Api.Tests.Persistence;

public class DemoSeedPasswordTests : IClassFixture<MediQueueApiFactory>
{
    /// <summary>The development password, published in the README.</summary>
    private const string PublicPassword = "MediQueue#2026";

    private readonly MediQueueApiFactory _factory;

    public DemoSeedPasswordTests(MediQueueApiFactory factory) => _factory = factory;

    private async Task<string> ResolveAsync(string? configured, bool isDevelopment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DemoSeedPassword.ConfigKey] = configured })
            .Build();

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        return await DemoSeedPassword.ResolveAsync(users, configuration, isDevelopment);
    }

    [Fact]
    public async Task Development_falls_back_to_the_documented_password()
    {
        Assert.Equal(PublicPassword, await ResolveAsync(null, isDevelopment: true));
    }

    [Fact]
    public async Task A_deployment_without_a_password_refuses_to_seed()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ResolveAsync(null, isDevelopment: false));

        Assert.Contains("Seed__StaffPassword", error.Message);
    }

    [Fact]
    public async Task The_public_readme_password_is_refused_on_a_deployment()
    {
        // The repository is public; this password must never open a live site.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ResolveAsync(PublicPassword, isDevelopment: false));

        Assert.Contains("public", error.Message);
    }

    [Fact]
    public async Task A_password_the_policy_would_reject_fails_before_anything_is_created()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ResolveAsync("short", isDevelopment: false));

        Assert.Contains("Seed__StaffPassword", error.Message);
        Assert.Contains("at least 10", error.Message);
    }

    [Fact]
    public async Task A_strong_configured_password_is_used_in_any_environment()
    {
        Assert.Equal("Str0ng#Password!", await ResolveAsync("Str0ng#Password!", isDevelopment: false));
        Assert.Equal("Str0ng#Password!", await ResolveAsync("Str0ng#Password!", isDevelopment: true));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/MediQueue.Api.Tests --filter "FullyQualifiedName~DemoSeedPasswordTests"`
Expected: build error `CS0103: The name 'DemoSeedPassword' does not exist`.

- [ ] **Step 3: Implement the password rule**

Create `src/MediQueue.Infrastructure/Seed/DemoSeedPassword.cs`:

```csharp
using MediQueue.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace MediQueue.Infrastructure.Seed;

/// <summary>
/// Decides the password every seeded staff account gets. The repository is
/// public, so its documented password must never open a deployed site; and a
/// password the policy rejects must stop the deploy, not produce a live site
/// on which nobody can sign in.
/// </summary>
public static class DemoSeedPassword
{
    public const string ConfigKey = "Seed:StaffPassword";

    public static async Task<string> ResolveAsync(
        UserManager<ApplicationUser> users,
        IConfiguration configuration,
        bool isDevelopment)
    {
        var configured = configuration[ConfigKey];

        if (string.IsNullOrWhiteSpace(configured))
        {
            if (isDevelopment)
            {
                return DemoData.StaffPassword;
            }

            throw new InvalidOperationException(
                "Demo data is enabled but no staff password is set. Set the Seed__StaffPassword config var.");
        }

        if (!isDevelopment && configured == DemoData.StaffPassword)
        {
            throw new InvalidOperationException(
                "Seed__StaffPassword is the development password published in the README, which is public. Choose another.");
        }

        // Identity's own validators, so this can never disagree with account creation.
        var problems = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, new ApplicationUser(), configured);
            problems.AddRange(result.Errors.Select(e => e.Description));
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Seed__StaffPassword does not meet the password policy: {string.Join(" ", problems)}");
        }

        return configured;
    }
}
```

- [ ] **Step 4: Run the new tests**

Run: `dotnet test tests/MediQueue.Api.Tests --filter "FullyQualifiedName~DemoSeedPasswordTests"`
Expected: `Passed: 5, Failed: 0`

- [ ] **Step 5: Make the seeder take the password and fail loudly**

In `src/MediQueue.Infrastructure/Seed/DemoDataSeeder.cs`:

Replace the signature line:

```csharp
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
```

with:

```csharp
    /// <param name="staffPassword">
    /// Already validated by <see cref="DemoSeedPassword"/>; every seeded staff account gets it.
    /// </param>
    public static async Task SeedAsync(
        IServiceProvider services,
        string staffPassword,
        CancellationToken cancellationToken = default)
```

Replace:

```csharp
        var staff = await SeedStaffAsync(users, departments, logger);
```

with:

```csharp
        var staff = await SeedStaffAsync(users, departments, staffPassword);
```

Replace the whole `SeedStaffAsync` method with:

```csharp
    private static async Task<List<ApplicationUser>> SeedStaffAsync(
        UserManager<ApplicationUser> users,
        List<Department> departments,
        string staffPassword)
    {
        var created = new List<ApplicationUser>();

        foreach (var seed in DemoData.Staff)
        {
            var user = new ApplicationUser
            {
                UserName = seed.Email,
                Email = seed.Email,
                EmailConfirmed = true,
                FullName = seed.FullName,
                DepartmentId = seed.DepartmentCode is null
                    ? null
                    : departments.First(d => d.Code == seed.DepartmentCode).Id
            };

            var result = await users.CreateAsync(user, staffPassword);

            // A demo hospital missing some of its staff is broken, not partial:
            // stop rather than log and carry on.
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create {seed.Email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
            }

            await users.AddToRoleAsync(user, seed.Role);
            created.Add(user);
        }

        return created;
    }
```

- [ ] **Step 6: Move startup's database work into one place**

Create `src/MediQueue.Api/DatabaseStartup.cs`:

```csharp
using MediQueue.Infrastructure.Identity;
using MediQueue.Infrastructure.Seed;
using Microsoft.AspNetCore.Identity;

namespace MediQueue.Api;

/// <summary>
/// Schema first, then, where enabled, the demo hospital. Runs on every start
/// and, on Heroku, on its own in the release phase. Both steps are no-ops once
/// done, so running twice is harmless.
/// </summary>
internal static class DatabaseStartup
{
    public static async Task RunAsync(WebApplication app, CancellationToken cancellationToken = default)
    {
        await DemoDataSeeder.MigrateAsync(app.Services, cancellationToken);

        // Demo patients only where they belong: on by default in Development,
        // elsewhere only when explicitly asked for.
        if (!app.Configuration.GetValue("Seed:DemoData", app.Environment.IsDevelopment()))
        {
            return;
        }

        string password;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            password = await DemoSeedPassword.ResolveAsync(
                users, app.Configuration, app.Environment.IsDevelopment());
        }

        await DemoDataSeeder.SeedAsync(app.Services, password, cancellationToken);
    }
}
```

In `src/MediQueue.Api/Program.cs` replace:

```csharp
// Schema always; demo patients only where they belong. A real deployment gets
// the tables and nothing else, so no fictional people can reach a live report.
await DemoDataSeeder.MigrateAsync(app.Services);

if (app.Configuration.GetValue("Seed:DemoData", app.Environment.IsDevelopment()))
{
    await DemoDataSeeder.SeedAsync(app.Services);
}

app.Run();
```

with:

```csharp
await DatabaseStartup.RunAsync(app);

app.Run();
```

and delete the now-unused `using MediQueue.Infrastructure.Seed;` at the top of `Program.cs`.

- [ ] **Step 7: Build and run everything**

Run: `dotnet build MediQueue.slnx` → `0 Warning(s)  0 Error(s)`; `dotnet test MediQueue.slnx` → all pass.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "Refuse to seed a deployment with a missing, weak or public password" -m "Seeded staff now take their password from Seed:StaffPassword. Outside Development a missing value, the README's public password, or one the Identity policy rejects stops startup with a message naming the config var. Previously a rejected password was logged and skipped, which would leave a live site nobody could sign in to."
```

---

### Task 9: A release-phase switch

**Files:**
- Modify: `src/MediQueue.Api/Program.cs`

- [ ] **Step 1: Strip the switch before configuration sees it**

In `Program.cs` replace:

```csharp
var builder = WebApplication.CreateBuilder(args);
```

with:

```csharp
// Heroku's release phase runs the app with this switch: migrate, seed, exit.
// Removed before configuration parses the arguments, so it is never read as a setting.
const string MigrateOnlySwitch = "--migrate-only";
var migrateOnly = args.Contains(MigrateOnlySwitch);

var builder = WebApplication.CreateBuilder(args.Where(arg => arg != MigrateOnlySwitch).ToArray());
```

- [ ] **Step 2: Exit after the database work when asked**

In `Program.cs` replace:

```csharp
await DatabaseStartup.RunAsync(app);

app.Run();
```

with:

```csharp
await DatabaseStartup.RunAsync(app);

if (migrateOnly)
{
    return;
}

app.Run();
```

- [ ] **Step 3: Verify against the local database**

Make sure no preview server is running (it locks DLLs), then:

```bash
dotnet run --project src/MediQueue.Api -- --migrate-only
```

Expected: logs `No migrations were applied…` and `Database already seeded…`, **no** `Now listening on`, and the process exits by itself with code 0.

- [ ] **Step 4: Run tests and commit**

Run: `dotnet test MediQueue.slnx` → all pass.

```bash
git add src/MediQueue.Api/Program.cs
git commit -m "Add a --migrate-only switch for Heroku's release phase" -m "The release phase migrates and seeds before new dynos start, so a failed migration cancels the deploy instead of crash-looping the web dyno, and first-boot seeding stays clear of Heroku's 60-second boot limit."
```

---

### Task 10: Recognise HTTPS behind Heroku's router

**Files:**
- Modify: `src/MediQueue.Api/Program.cs`
- Test: `tests/MediQueue.Api.Tests/ForwardedHeadersTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/MediQueue.Api.Tests/ForwardedHeadersTests.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MediQueue.Api.Tests;

public class ForwardedHeadersTests : IClassFixture<MediQueueApiFactory>
{
    private readonly MediQueueApiFactory _factory;

    public ForwardedHeadersTests(MediQueueApiFactory factory) => _factory = factory;

    private HttpClient Client(bool trustForwardedHeaders)
    {
        WebApplicationFactory<Program> factory = trustForwardedHeaders
            ? _factory.WithWebHostBuilder(b => b.UseSetting("Hosting:TrustForwardedHeaders", "true"))
            : _factory;

        return factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private static HttpRequestMessage ViaRouter(string forwardedProto)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/departments");
        request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        return request;
    }

    [Fact]
    public async Task Behind_the_router_plain_http_is_sent_to_https()
    {
        var response = await Client(trustForwardedHeaders: true).SendAsync(ViaRouter("http"));

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https", response.Headers.Location!.Scheme);
    }

    [Fact]
    public async Task Behind_the_router_https_is_served()
    {
        var response = await Client(trustForwardedHeaders: true).SendAsync(ViaRouter("https"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_trusted_proxy_the_headers_are_ignored()
    {
        // A client talking to the app directly must not be able to steer it by
        // claiming to have come through a proxy.
        var response = await Client(trustForwardedHeaders: false).SendAsync(ViaRouter("http"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run to verify the right one fails**

Run: `dotnet test tests/MediQueue.Api.Tests --filter "FullyQualifiedName~ForwardedHeadersTests"`
Expected: `Behind_the_router_plain_http_is_sent_to_https` FAILS (`Expected: TemporaryRedirect, Actual: OK`); the other two pass, because today the headers are ignored everywhere.

- [ ] **Step 3: Implement**

In `Program.cs` add to the `using` block:

```csharp
using Microsoft.AspNetCore.HttpOverrides;
```

Directly after `builder.Services.AddOpenApi();` add:

```csharp
// Heroku terminates TLS at its router and forwards plain HTTP. Trusting its
// X-Forwarded-* headers is how the app learns a request arrived over HTTPS, so
// HSTS is sent and http:// is redirected. Off unless configured, because a
// client talking to the app directly could otherwise claim anything.
var behindProxy = builder.Configuration.GetValue<bool>("Hosting:TrustForwardedHeaders");

if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // The router has no fixed address to allow-list. The default forward
        // limit of one still takes only the entry the router appended.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

    builder.Services.AddHttpsRedirection(options => options.HttpsPort = 443);
}
```

Replace:

```csharp
var app = builder.Build();

app.UseExceptionHandler();
```

with:

```csharp
var app = builder.Build();

// First, so everything after it sees the scheme the browser actually used.
if (behindProxy)
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/MediQueue.Api.Tests --filter "FullyQualifiedName~ForwardedHeadersTests"` → `Passed: 3`
Run: `dotnet build MediQueue.slnx` → `0 Warning(s)`; `dotnet test MediQueue.slnx` → all pass.

- [ ] **Step 5: Commit**

```bash
git add src/MediQueue.Api/Program.cs tests/MediQueue.Api.Tests/ForwardedHeadersTests.cs
git commit -m "Recognise HTTPS behind Heroku's router" -m "Heroku forwards plain HTTP, so without trusting X-Forwarded-Proto the app never sent HSTS and never redirected http. Trust is opt-in through Hosting:TrustForwardedHeaders, because a client reaching the app directly could otherwise claim any scheme."
```

---

### Task 11: Tell Heroku what to build and how to run it

**Files:**
- Create: `Procfile`
- Create: `project.toml`
- Create: `.gitattributes`
- Modify: `.editorconfig`

- [ ] **Step 1: Procfile**

Create `Procfile` (no extension) at the repository root, exactly:

```
release: cd src/MediQueue.Api/bin/publish && ./MediQueue.Api --migrate-only
web: cd src/MediQueue.Api/bin/publish && ./MediQueue.Api --urls http://*:$PORT
```

Both `cd` first because ASP.NET Core finds `appsettings.json` and the Blazor `wwwroot` relative to the working directory; this mirrors the buildpack's own generated command. An explicit Procfile is required for the `release` entry, and once present the buildpack generates none.

- [ ] **Step 2: project.toml**

Create `project.toml` at the repository root:

```toml
# Heroku's .NET buildpack publishes what this names. The API alone keeps the
# test projects out of the slug and builds the Blazor client once, as a
# reference. Publishing the whole solution races on the client's static-asset
# cache and fails intermittently.
[_]
schema-version = "0.2"

[com.heroku.buildpacks.dotnet]
solution_file = "src/MediQueue.Api/MediQueue.Api.csproj"
```

- [ ] **Step 3: Keep the Procfile's line endings Unix**

A CRLF Procfile makes `cd` receive a directory name ending in `\r`. This machine converts on commit, but a teammate's might not, so pin it.

Create `.gitattributes`:

```
* text=auto
Procfile text eol=lf
*.sh text eol=lf
```

Append to `.editorconfig`:

```ini

[Procfile]
end_of_line = lf
```

- [ ] **Step 4: Verify endings in the index**

```bash
git add --renormalize .
git add Procfile project.toml .gitattributes .editorconfig
git ls-files --eol Procfile project.toml
```

Expected: both show `i/lf`. If `--renormalize` staged unrelated files, inspect `git status`: only line-ending normalisation is acceptable in them, and it belongs in this commit.

- [ ] **Step 5: Commit**

```bash
git commit -m "Tell Heroku what to build and how to run it" -m "project.toml points the buildpack at the API project, because publishing the whole solution races on the Blazor client's asset cache. The Procfile adds the release phase and enters the publish folder first, so configuration and the Blazor files are found. The Procfile is pinned to LF, since a CRLF one breaks the cd."
```

---

### Task 12: Rehearse the Heroku sequence locally

Heroku publishes for Linux; this machine is Windows. Publishing for `win-x64` to the same relative folder and running the Procfile's commands from it proves the working-directory assumption, `--migrate-only`, the password guard, forwarded headers and static files, before any real deploy.

**Files:** none (verification only).

- [ ] **Step 1: Check disk, then publish**

Stop any preview server first.

```powershell
Get-PSDrive C | Select-Object @{n='FreeGB';e={[math]::Round($_.Free/1GB,2)}}
cd C:\Users\eotoo\projects\MediQueue
$art = "C:\Users\eotoo\AppData\Local\Temp\claude\C--Users-eotoo-projects-MediQueue\9fc06bee-aea2-4e34-ba56-a06e8dec9f2f\scratchpad\artifacts"
dotnet publish src/MediQueue.Api/MediQueue.Api.csproj --runtime win-x64 -p:PublishDir=bin/publish --configuration Release --artifacts-path $art
```

Expected: at least 0.4 GB free before; `MediQueue.Api -> …\src\MediQueue.Api\bin\publish\`, exit 0, no warnings.

Shell state does not persist between tool calls, so **each of Steps 2–4 is one PowerShell call that starts with this block**:

```powershell
cd C:\Users\eotoo\projects\MediQueue\src\MediQueue.Api\bin\publish
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:Jwt__Key = "rehearsal-signing-key-that-is-long-enough-for-hmac-sha256"
$env:Seed__DemoData = "true"
$env:Hosting__TrustForwardedHeaders = "true"
```

- [ ] **Step 2: The release command refuses to run without a password**

```powershell
Remove-Item Env:Seed__StaffPassword -ErrorAction SilentlyContinue
.\MediQueue.Api.exe --migrate-only; "exit: $LASTEXITCODE"
```

Expected: an unhandled `InvalidOperationException` naming `Seed__StaffPassword`, and a non-zero exit code. This is what makes a misconfigured Heroku release fail instead of shipping.

- [ ] **Step 3: With a password, it migrates, seeds and exits**

```powershell
$env:Seed__StaffPassword = "Rehearsal#Pass2026"
.\MediQueue.Api.exe --migrate-only; "exit: $LASTEXITCODE"
Test-Path .\mediqueue.db
```

Expected: `Applying migration '20260814224156_InitialSchema'`, `Seeded 6 departments, 11 staff, 180 patients.`, `exit: 0`, `True`.

- [ ] **Step 4: The web command serves the app from the publish folder**

`curl.exe`, not `curl`: in Windows PowerShell `curl` is an alias for `Invoke-WebRequest`.

```powershell
$env:Seed__StaffPassword = "Rehearsal#Pass2026"
$p = Start-Process -FilePath .\MediQueue.Api.exe -ArgumentList '--urls','http://localhost:5299' -PassThru -WindowStyle Hidden
try {
    for ($i = 0; $i -lt 30; $i++) { if ((curl.exe -s -o NUL -w "%{http_code}" http://localhost:5299/api/departments) -eq "200") { break }; Start-Sleep 1 }

    $https = "X-Forwarded-Proto: https"
    "departments: " + ((curl.exe -s -H $https http://localhost:5299/api/departments | ConvertFrom-Json).Count)

    $index = curl.exe -s -H $https http://localhost:5299/
    $script = [regex]::Match(($index -join "`n"), '_framework/blazor\.webassembly[^"]*\.js').Value
    "script: $script -> " + (curl.exe -s -o NUL -w "%{http_code}" -H $https "http://localhost:5299/$script")

    "http -> " + (curl.exe -s -o NUL -w "%{http_code} %{redirect_url}" -H "X-Forwarded-Proto: http" http://localhost:5299/)
}
finally { Stop-Process -Id $p.Id -Force }
```

Expected:
- `departments: 6`
- `script: _framework/blazor.webassembly.<hash>.js -> 200`. This is the check that the fingerprinted Blazor script named in the published `index.html` is actually served.
- `http -> 307 https://localhost/`

HSTS is not checked here. ASP.NET Core never sends it for `localhost`, so Task 16 checks it on the live site instead.

Signing in is not part of this rehearsal. Task 8's tests already prove which passwords are accepted.

- [ ] **Step 5: Clean up**

```powershell
cd C:\Users\eotoo\projects\MediQueue
Remove-Item -Recurse -Force src\MediQueue.Api\bin\publish, "C:\Users\eotoo\AppData\Local\Temp\claude\C--Users-eotoo-projects-MediQueue\9fc06bee-aea2-4e34-ba56-a06e8dec9f2f\scratchpad\artifacts"
git status --short
Get-PSDrive C | Select-Object @{n='FreeGB';e={[math]::Round($_.Free/1GB,2)}}
```

Expected: working tree clean (`bin/` is ignored) and disk back to its pre-publish level.

- [ ] **Step 6: If any check failed**

Use superpowers:systematic-debugging. Fix in the relevant earlier task's files, add a test where the behaviour is testable in-process, commit, and rerun this task from Step 1.

---

### Task 13: Point docker-compose at Postgres

**Files:**
- Modify: `docker-compose.yml`

- [ ] **Step 1: Replace the file**

```yaml
services:
  db:
    image: postgres:17
    environment:
      POSTGRES_USER: mediqueue
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in a .env file}
      POSTGRES_DB: mediqueue
    volumes:
      - pg-data:/var/lib/postgresql/data
    healthcheck:
      # The API migrates on startup, so it must not race the server's boot.
      test: ["CMD-SHELL", "pg_isready -U mediqueue -d mediqueue"]
      interval: 5s
      timeout: 5s
      retries: 12

  api:
    build: .
    depends_on:
      db:
        condition: service_healthy
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      Database__Provider: Postgres
      ConnectionStrings__Default: Host=db;Database=mediqueue;Username=mediqueue;Password=${POSTGRES_PASSWORD}
      # Required outside Development; there is no fallback in production.
      Jwt__Key: ${JWT_KEY:?set JWT_KEY in a .env file}
      # The same demo hospital as Heroku, behind a password of your choosing.
      Seed__DemoData: "true"
      Seed__StaffPassword: ${SEED_STAFF_PASSWORD:?set SEED_STAFF_PASSWORD in a .env file}
    ports:
      - "8080:8080"

volumes:
  pg-data:
```

The database port is deliberately not published to the host. The previous compose file seeded nothing, which left no account able to sign in.

- [ ] **Step 2: Commit**

This cannot be run here (no Docker); say so in the final report.

```bash
git add docker-compose.yml
git commit -m "Point docker-compose at Postgres" -m "SQL Server is gone. The compose stack now matches Heroku: Postgres, and a seeded demo hospital behind a password from the .env file. The old file seeded nothing, so no account could sign in. Untested: Docker is not available on the development machine."
```

---

### Task 14: README: deployment runbook and configuration

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Fix the stack line**

Replace:

```markdown
Blazor WebAssembly · ASP.NET Core Web API · SignalR · Entity Framework Core · SQL Server (SQLite in development) · .NET 10
```

with:

```markdown
Blazor WebAssembly · ASP.NET Core Web API · SignalR · Entity Framework Core · PostgreSQL (SQLite in development) · .NET 10
```

- [ ] **Step 2: Fix the sign-in note**

Replace:

```markdown
Every seeded account uses the password `MediQueue#2026`. Development only — the seeder never runs outside Development unless `Seed:DemoData` is explicitly set.
```

with:

```markdown
Locally, every seeded account uses the password `MediQueue#2026`. It is published here, so a deployed site refuses it: deployments must set their own through `Seed:StaffPassword`.
```

- [ ] **Step 3: Replace the Configuration and Deploying sections**

Replace everything from `## Configuration` up to (not including) `## Team` with:

````markdown
## Configuration

| Setting | Default | Notes |
| --- | --- | --- |
| `Database:Provider` | `Sqlite` | `Postgres` in deployment. Any other value stops startup. |
| `ConnectionStrings:Default` | `Data Source=mediqueue.db` | Used unless `DATABASE_URL` is set |
| `DATABASE_URL` | — | Set by Heroku Postgres; takes precedence when the provider is Postgres |
| `Jwt:Key` | generated per run in Development | **Required** outside Development; startup fails without it |
| `Seed:DemoData` | `true` in Development only | Seeds the demo hospital on an empty database |
| `Seed:StaffPassword` | README password in Development only | **Required** when seeding outside Development. Must meet the password policy and must not be the README password. |
| `Hosting:TrustForwardedHeaders` | `false` | `true` behind Heroku's router, so HTTPS is recognised |

Environment variables use double underscores: `Seed__StaffPassword`.

## Deploying to Heroku

The app runs on one Basic dyno ($7/month) with Heroku Postgres Essential-0 ($5/month). The [GitHub Student Developer Pack](https://www.heroku.com/github-students/) gives $13/month of Heroku credit for 24 months, which covers both. Heroku requires a card on file even when credit covers the bill.

Everything below is done in the Heroku dashboard; no CLI is needed.

**1. Create the app.** [dashboard.heroku.com](https://dashboard.heroku.com) → **New** → **Create new app**. Choose a name and the **Europe** region, which is closer to Ghana than the United States.

**2. Add the database.** **Resources** → **Add-ons** → search *Heroku Postgres* → plan **Essential 0** → **Submit Order Form**. This sets `DATABASE_URL`.

**3. Set the config vars.** **Settings** → **Reveal Config Vars**, then add:

| Key | Value |
| --- | --- |
| `Database__Provider` | `Postgres` |
| `Jwt__Key` | a random value you generate (below) |
| `Seed__DemoData` | `true` |
| `Seed__StaffPassword` | a password you choose: 10+ characters, an uppercase letter, a digit and a symbol, and not the README password |
| `Hosting__TrustForwardedHeaders` | `true` |

Generate `Jwt__Key` on your own machine, so it never passes through chat or the repository:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

```bash
openssl rand -base64 48
```

Share `Seed__StaffPassword` with the team privately. Every seeded staff account on the live site uses it.

**4. Connect GitHub.** **Deploy** → **Deployment method: GitHub** → connect → find `MediQueue` → **Connect**. Under **Automatic deploys**, choose `main`, tick **Wait for CI to pass before deploy**, then **Enable Automatic Deploys**.

**5. First deploy.** Under **Manual deploy**, choose `main` → **Deploy Branch**. The build log should show `dotnet publish` of `src/MediQueue.Api/MediQueue.Api.csproj`. The release log should end with `Seeded 6 departments, 11 staff, 180 patients.`

**6. Use a Basic dyno.** **Resources** → **Change Dyno Type** → **Basic**. Eco dynos sleep after 30 minutes idle, and the first visitor afterwards waits while it wakes. That's bad in a demo.

**7. Open the app.** Click **Open app**.

After that, every push to `main` deploys once CI passes. Migrations run in the release phase, before the new version takes traffic. A failed migration cancels the deploy and leaves the running version alone.

### When something goes wrong

**Activity** → the failed build or release → **View log**.

| Symptom | Cause |
| --- | --- |
| Release fails naming `Seed__StaffPassword` | The var is missing, too weak, or the README password |
| App crashes with `Jwt:Key is not configured` | `Jwt__Key` is missing |
| Startup fails with `Unknown Database:Provider` | `Database__Provider` is misspelt; it must be `Postgres` |
| Build log publishes `MediQueue.slnx` instead of the API project | `project.toml` was not picked up. Add config var `SOLUTION_FILE` = `src/MediQueue.Api/MediQueue.Api.csproj` |

## Running with Docker

```bash
docker compose up --build
```

Needs a `.env` file with `POSTGRES_PASSWORD`, `JWT_KEY` and `SEED_STAFF_PASSWORD`; Compose refuses to start without them. The stack mirrors Heroku: Postgres and the seeded demo hospital, served on port 8080.

CI builds and tests on every push, once on SQLite and once against a real Postgres, and fails if a model change has no matching migration.
````

- [ ] **Step 4: Check nothing still claims SQL Server**

```bash
grep -rn -i "sql server\|sqlserver\|mssql" README.md docker-compose.yml src .github
```

Expected: no output. (Historical specs under `docs/` may mention SQL Server; they are dated records and stay as written.)

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "Document Heroku deployment and the new configuration" -m "A click-by-click runbook for the dashboard, the config vars with what each one guards, and a troubleshooting table. Removes the claims that SQL Server and the old compose setup worked."
```

---

### Task 15: Final verification and merge

- [ ] **Step 1: Everything green locally**

```bash
dotnet build MediQueue.slnx
dotnet test MediQueue.slnx
git status --short
```

Expected: `0 Warning(s)  0 Error(s)`; Domain 53, Client 27, API 61 passed (37 existing + 9 connection-string + 5 provider + 2 provider-guard + 5 seed-password + 3 forwarded-headers; recount if tests were added while fixing); clean tree.

- [ ] **Step 2: CI green on the branch head**

```bash
git push
```

Then repeat Task 7 Steps 2–3 for the new head. Expected: both jobs `success`.

- [ ] **Step 3: Merge and push main**

Heroku is not connected yet, so this deploys nothing. It makes `main` ready for the user's first manual deploy.

```bash
git checkout main
git merge --ff-only feat/heroku-deploy
git push origin main
```

Then confirm CI on `main` (Task 7 Steps 2–3 with `branch=main`). Expected: both jobs `success`.

- [ ] **Step 4: Delete the merged branch**

```bash
git branch -d feat/heroku-deploy
git push origin --delete feat/heroku-deploy
```

---

### Task 16: First deploy and live smoke test

**The user does the setup.** It needs their identity, a card, and secrets that must not pass through the assistant.

- [ ] **Step 1: Hand over the runbook**

Point the user to README → *Deploying to Heroku*, steps 1–7. Wait for them to report the app's URL.

- [ ] **Step 2: Anonymous checks against the live URL**

Set `$site` to the URL the user reports, then:

```powershell
curl.exe -s -o NUL -w "%{http_code}`n" "$site/"                                   # 200
curl.exe -s -D - -o NUL "$site/" | Select-String -Pattern "strict-transport-security"   # present
(curl.exe -s "$site/api/departments" | ConvertFrom-Json).Count                    # 6
curl.exe -s -o NUL -w "%{http_code} %{redirect_url}`n" ($site -replace '^https','http')  # 307 https://…
```

- [ ] **Step 3: The real-time flow, in the browser pane**

1. Open `$site/checkin` in one tab and check in a test patient. Note the ticket code.
2. Ask the user to sign in as a receptionist in a second tab of the browser pane. The assistant does not enter passwords.
3. Confirm the patient appears on `/reception` with no refresh.
4. Open `/ticket/<code>` in the first tab; call the patient from reception; confirm the ticket tab changes to *It is your turn* by itself.
5. Open `/board/<departmentId>` and confirm the code and room appear with no names.
6. Ask the user to confirm the README password is refused on the live sign-in page.

- [ ] **Step 4: Report**

Report what was verified, with evidence, and what was not: the Docker files remain untested.
