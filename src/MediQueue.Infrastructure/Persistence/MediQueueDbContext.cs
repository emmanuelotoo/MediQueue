using MediQueue.Domain.Entities;
using MediQueue.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MediQueue.Infrastructure.Persistence;

public class MediQueueDbContext : IdentityDbContext<ApplicationUser>
{
    public MediQueueDbContext(DbContextOptions<MediQueueDbContext> options) : base(options)
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
    }
}
