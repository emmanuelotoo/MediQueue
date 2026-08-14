using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MediQueue.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> at design time. Migrations are generated
/// against SQLite, the development provider; the same migrations are applied to
/// SQL Server in deployment, so nothing here is provider-specific.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MediQueueDbContext>
{
    public MediQueueDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MediQueueDbContext>()
            .UseSqlite("Data Source=mediqueue-design.db")
            .Options;

        return new MediQueueDbContext(options);
    }
}
