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
