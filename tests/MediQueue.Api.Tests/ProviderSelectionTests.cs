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
