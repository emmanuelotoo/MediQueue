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
    /// Registers persistence and identity. The database provider is chosen by
    /// <c>Database:Provider</c>: SQLite by default so a fresh clone runs with no
    /// setup, SQL Server in deployment.
    /// </summary>
    public static IServiceCollection AddMediQueueInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("Default")
            ?? "Data Source=mediqueue.db";

        services.AddDbContext<MediQueueDbContext>(options =>
        {
            if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlServer(connectionString);
            }
            else
            {
                options.UseSqlite(connectionString);
            }
        });

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
}
