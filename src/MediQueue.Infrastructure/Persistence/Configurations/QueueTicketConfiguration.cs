using MediQueue.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediQueue.Infrastructure.Persistence.Configurations;

public class QueueTicketConfiguration : IEntityTypeConfiguration<QueueTicket>
{
    public void Configure(EntityTypeBuilder<QueueTicket> builder)
    {
        builder.ToTable("Tickets");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TicketCode).IsRequired().HasMaxLength(16);
        builder.Property(t => t.RoomNumber).HasMaxLength(10);
        builder.Property(t => t.AssignedStaffId).HasMaxLength(450);
        builder.Property(t => t.Notes).HasMaxLength(1000);

        builder.Property(t => t.Priority).HasConversion<int>();
        builder.Property(t => t.Status).HasConversion<int>();

        // Computed from timestamps; never stored.
        builder.Ignore(t => t.WaitDuration);
        builder.Ignore(t => t.ServiceDuration);

        builder.HasOne(t => t.Patient)
            .WithMany(p => p.Tickets)
            .HasForeignKey(t => t.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Department)
            .WithMany(d => d.Tickets)
            .HasForeignKey(t => t.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Every console load asks "what is waiting in this department today".
        builder.HasIndex(t => new { t.DepartmentId, t.Status, t.CheckedInAt });

        // Patients look themselves up by the code on their slip.
        builder.HasIndex(t => t.TicketCode);
    }
}
