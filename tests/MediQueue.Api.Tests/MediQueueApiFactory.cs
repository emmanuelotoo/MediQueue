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
