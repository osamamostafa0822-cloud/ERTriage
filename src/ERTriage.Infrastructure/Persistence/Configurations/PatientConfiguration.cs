using System.Globalization;
using ERTriage.Domain.Patients;
using ERTriage.Domain.Triage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERTriage.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Patient"/> aggregate to the single <c>Patients</c> table (D-03) together with its owned
/// <see cref="VitalSigns"/> value object (D-37). Column sizes and CHECK limits come from the Domain constants
/// (<see cref="PatientRules"/>, <see cref="VitalSignsLimits"/>, <see cref="TriagePriority"/>) so numbers are never duplicated.
/// The CHECK constraints are a safety net; the Domain remains the source of truth for every rule.
/// </summary>
internal sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public const string TableName = "Patients";
    public const string BoardIndexName = "IX_Patients_Status_CurrentPriority_ArrivalTime_Id";

    // Longest priority reason produced by the calculator (docs/02-architecture/database-design.md).
    private const int PriorityReasonMaxLength = 200;
    private const int StatusMaxLength = 20;

    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable(TableName, ConfigureCheckConstraints);

        builder.HasKey(p => p.Id);

        // The Domain assigns the Guid (v7) in Patient.Register; the database never generates it.
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.FullName)
            .IsRequired()
            .HasMaxLength(PatientRules.FullNameMaxLength);

        builder.Property(p => p.Age)
            .HasColumnType("tinyint");

        builder.Property(p => p.ChiefComplaint)
            .IsRequired()
            .HasMaxLength(PatientRules.ChiefComplaintMaxLength);

        builder.OwnsOne(p => p.Vitals, ConfigureVitals);
        builder.Navigation(p => p.Vitals).IsRequired();

        // DateTime columns use datetime2 and the UTC converters registered in ErTriageDbContext.ConfigureConventions.
        builder.Property(p => p.ArrivalTime).IsRequired();
        builder.Property(p => p.CreatedAt).IsRequired();

        builder.Property(p => p.CalculatedPriority)
            .HasColumnType("tinyint");

        builder.Property(p => p.PriorityReason)
            .IsRequired()
            .HasMaxLength(PriorityReasonMaxLength);

        builder.Property(p => p.OverridePriority)
            .HasColumnType("tinyint");

        builder.Property(p => p.OverrideReason)
            .HasMaxLength(PatientRules.OverrideReasonMaxLength);

        builder.Property(p => p.OverriddenBy)
            .HasMaxLength(PatientRules.OverriddenByMaxLength);

        // Domain-maintained (D-33): only RefreshCurrentPriority assigns it; a CHECK guards the formula.
        builder.Property(p => p.CurrentPriority)
            .HasColumnType("tinyint");

        // Enum stored as its name for readable data; the CHECK below restricts it to the known names.
        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(StatusMaxLength)
            .IsRequired();

        builder.Property(p => p.DispositionNote)
            .HasMaxLength(PatientRules.DispositionNoteMaxLength);

        // Optimistic concurrency (SQL Server rowversion); a conflict becomes a 409 via UnitOfWork.
        builder.Property(p => p.RowVersion)
            .IsRowVersion();

        // Derived from OverridePriority; not stored.
        builder.Ignore(p => p.IsOverridden);

        // Board query: filter by status, then priority, arrival time and a deterministic tie-breaker.
        builder.HasIndex(p => new { p.Status, p.CurrentPriority, p.ArrivalTime, p.Id })
            .HasDatabaseName(BoardIndexName);
    }

    private static void ConfigureVitals(OwnedNavigationBuilder<Patient, VitalSigns> vitals)
    {
        // Same table as Patients; columns are deliberately unprefixed (database-design.md).
        vitals.Property(v => v.HeartRate).HasColumnName("HeartRate").HasColumnType("smallint");
        vitals.Property(v => v.SystolicBp).HasColumnName("SystolicBp").HasColumnType("smallint");
        vitals.Property(v => v.DiastolicBp).HasColumnName("DiastolicBp").HasColumnType("smallint");
        vitals.Property(v => v.TemperatureC).HasColumnName("TemperatureC").HasPrecision(4, 1);
        vitals.Property(v => v.SpO2).HasColumnName("SpO2").HasColumnType("tinyint");
    }

    private static void ConfigureCheckConstraints(TableBuilder<Patient> table)
    {
        var statuses = string.Join(", ", Enum.GetNames<PatientStatus>().Select(name => $"'{name}'"));

        table.HasCheckConstraint("CK_Patients_Age", Between("Age", PatientRules.AgeMin, PatientRules.AgeMax));

        table.HasCheckConstraint("CK_Patients_HeartRate",
            Between("HeartRate", VitalSignsLimits.HeartRateMin, VitalSignsLimits.HeartRateMax));
        table.HasCheckConstraint("CK_Patients_SystolicBp",
            Between("SystolicBp", VitalSignsLimits.SystolicBpMin, VitalSignsLimits.SystolicBpMax));
        table.HasCheckConstraint("CK_Patients_DiastolicBp",
            Between("DiastolicBp", VitalSignsLimits.DiastolicBpMin, VitalSignsLimits.DiastolicBpMax));
        table.HasCheckConstraint("CK_Patients_TemperatureC",
            Between("TemperatureC", VitalSignsLimits.TemperatureCMin, VitalSignsLimits.TemperatureCMax));
        table.HasCheckConstraint("CK_Patients_SpO2",
            Between("SpO2", VitalSignsLimits.SpO2Min, VitalSignsLimits.SpO2Max));
        table.HasCheckConstraint("CK_Patients_DiastolicBelowSystolic", "[DiastolicBp] < [SystolicBp]");

        table.HasCheckConstraint("CK_Patients_CalculatedPriority",
            Between("CalculatedPriority", TriagePriority.MostUrgent, TriagePriority.LeastUrgent));
        // NULL passes a BETWEEN check, so this only constrains an override that exists.
        table.HasCheckConstraint("CK_Patients_OverridePriority",
            Between("OverridePriority", TriagePriority.MostUrgent, TriagePriority.LeastUrgent));

        // An override always carries its reason and timestamp; without an override they must be empty.
        table.HasCheckConstraint("CK_Patients_OverrideFieldsConsistent",
            "([OverridePriority] IS NULL AND [OverrideReason] IS NULL AND [OverriddenAt] IS NULL) " +
            "OR ([OverridePriority] IS NOT NULL AND [OverrideReason] IS NOT NULL AND [OverriddenAt] IS NOT NULL)");

        table.HasCheckConstraint("CK_Patients_CurrentPriority",
            "[CurrentPriority] = COALESCE([OverridePriority], [CalculatedPriority])");

        table.HasCheckConstraint("CK_Patients_Status", $"[Status] IN ({statuses})");
    }

    private static string Between(string column, int min, int max) =>
        string.Create(CultureInfo.InvariantCulture, $"[{column}] BETWEEN {min} AND {max}");

    private static string Between(string column, decimal min, decimal max) =>
        string.Create(CultureInfo.InvariantCulture, $"[{column}] BETWEEN {min} AND {max}");
}
