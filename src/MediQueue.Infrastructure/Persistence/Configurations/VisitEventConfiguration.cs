using MediQueue.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediQueue.Infrastructure.Persistence.Configurations;

public class VisitEventConfiguration : IEntityTypeConfiguration<VisitEvent>
{
    public void Configure(EntityTypeBuilder<VisitEvent> builder)
    {
        builder.ToTable("VisitEvents");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).HasConversion<int>();
        builder.Property(e => e.ActorUserId).HasMaxLength(450);
        builder.Property(e => e.Metadata).HasMaxLength(1000);

        builder.HasOne(e => e.Ticket)
            .WithMany(t => t.Events)
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        // Reporting scans this table by date.
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => new { e.TicketId, e.OccurredAt });
    }
}
