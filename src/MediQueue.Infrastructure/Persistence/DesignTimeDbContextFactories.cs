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
