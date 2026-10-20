using ERTriage.Domain.Patients;
using ERTriage.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERTriage.Infrastructure.Persistence.Configurations;

/// <summary>Maps the Development seed marker table (D-58). Additive: the <c>Patients</c> table is not changed.</summary>
internal sealed class DevelopmentSeedRecordConfiguration : IEntityTypeConfiguration<DevelopmentSeedRecord>
{
    public void Configure(EntityTypeBuilder<DevelopmentSeedRecord> builder)
    {
        builder.ToTable("DevelopmentSeedRecords");

        builder.HasKey(r => r.SeedKey).HasName("PK_DevelopmentSeedRecords");

        builder.Property(r => r.SeedKey)
            .HasMaxLength(FictionalPatientSeedValidator.SeedKeyMaxLength)
            .IsUnicode(false)
            .ValueGeneratedNever();

        builder.Property(r => r.PatientId).IsRequired();
        builder.Property(r => r.SeededAt).IsRequired();

        builder.HasIndex(r => r.PatientId)
            .IsUnique()
            .HasDatabaseName("UX_DevelopmentSeedRecords_PatientId");

        // Deleting a fictional patient removes its marker, so the next Development start recreates it with the same key.
        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(r => r.PatientId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_DevelopmentSeedRecords_Patients_PatientId");
    }
}
