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
