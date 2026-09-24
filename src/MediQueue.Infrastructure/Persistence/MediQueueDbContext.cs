using System.Globalization;
using MediQueue.Domain.Entities;
using MediQueue.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MediQueue.Infrastructure.Persistence;

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

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<QueueTicket> Tickets => Set<QueueTicket>();
    public DbSet<VisitEvent> VisitEvents => Set<VisitEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(MediQueueDbContext).Assembly);

        if (Database.IsSqlite())
        {
            ApplySqliteTimestampConversion(builder);
        }
    }

    /// <summary>
    /// SQLite has no native offset-aware date type, and EF cannot translate a
    /// comparison against one — which every "today's queue" query needs. Storing
    /// a fixed-width UTC string keeps lexicographic order identical to
    /// chronological order, so <c>&gt;=</c> and <c>ORDER BY</c> both work in the
    /// database rather than being pulled into memory. Postgres stores
    /// <c>timestamp with time zone</c> natively and never sees this.
    /// </summary>
    private static void ApplySqliteTimestampConversion(ModelBuilder builder)
    {
        var converter = new ValueConverter<DateTimeOffset, string>(
            value => value.ToUniversalTime()
                .ToString("yyyy-MM-ddTHH:mm:ss.fffffff+00:00", CultureInfo.InvariantCulture),
            value => DateTimeOffset.Parse(
                value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

        foreach (var property in builder.Model.GetEntityTypes()
                     .SelectMany(entity => entity.GetProperties())
                     .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
        {
            property.SetValueConverter(converter);
        }
    }
}
