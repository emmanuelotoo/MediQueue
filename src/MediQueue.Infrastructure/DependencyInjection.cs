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
            // Heroku sets DATABASE_URL when Postgres is attached. SQLite there
            // would keep the data on a disk that is wiped at least once a day.
            if (!string.IsNullOrWhiteSpace(configuration["DATABASE_URL"]))
            {
                throw new InvalidOperationException(
                    "DATABASE_URL is set, so a Postgres database is attached, but the app is set to use SQLite "
                    + "(Database:Provider is unset or Sqlite). Set the config var Database__Provider to Postgres.");
            }

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
