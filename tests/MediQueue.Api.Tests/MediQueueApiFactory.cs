using MediQueue.Domain.Entities;
using MediQueue.Infrastructure.Identity;
using MediQueue.Infrastructure.Persistence;
using MediQueue.Shared.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MediQueue.Api.Tests;

/// <summary>
/// Hosts the real pipeline â€” real routing, real authorization, real SignalR â€”
/// against an in-memory SQLite database. Only the storage is swapped, so the
/// tests exercise the same code that runs in production.
/// </summary>
public class MediQueueApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "TestPass#2026";

    private static int _departmentSequence;

    /// <summary>
    /// A department code no other test in the run has used. Codes carry a
    /// unique index, and the random two-digit codes used before collided often
    /// enough to fail roughly one CI run in ten.
    /// </summary>
    public static string UniqueDepartmentCode() =>
        $"T{Interlocked.Increment(ref _departmentSequence):D4}";

    /// <summary>
    /// Held open for the lifetime of the factory: an in-memory SQLite database
    /// is discarded the moment its last connection closes.
    /// </summary>
    private SqliteConnection _connection = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("Seed:DemoData", "false");
        builder.UseSetting("Jwt:Key", "test-signing-key-that-is-long-enough-for-hmac-sha256");

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSimpleConsole(o => o.SingleLine = false);
            logging.SetMinimumLevel(LogLevel.Warning);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<MediQueueDbContext>>();
            services.RemoveAll<MediQueueDbContext>();

            services.AddDbContext<MediQueueDbContext>(options => options.UseSqlite(_connection));
        });
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediQueueDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
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
