using MediQueue.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediQueue.Infrastructure.Persistence.Configurations;

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.MedicalRecordNumber).IsRequired().HasMaxLength(32);
        builder.Property(p => p.FullName).IsRequired().HasMaxLength(120);
        builder.Property(p => p.PhoneNumber).IsRequired().HasMaxLength(20);
        builder.Property(p => p.Gender).HasMaxLength(20);

        builder.HasIndex(p => p.MedicalRecordNumber).IsUnique();

        // Returning patients are found by phone number at the kiosk.
        builder.HasIndex(p => p.PhoneNumber);
    }
}
