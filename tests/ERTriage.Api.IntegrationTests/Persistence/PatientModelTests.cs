using ERTriage.Domain.Patients;
using ERTriage.Infrastructure.Persistence;
using ERTriage.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ERTriage.Api.IntegrationTests.Persistence;

/// <summary>
/// Verifies the EF Core model (table, columns, constraints, index, concurrency token) without a database:
/// building the model never opens a connection. Real SQL Server behaviour is verified when migrations exist (M3).
/// </summary>
public sealed class PatientModelTests : IDisposable
{
    private readonly ErTriageDbContext _context;
    private readonly IModel _model;

    public PatientModelTests()
    {
        var options = new DbContextOptionsBuilder<ErTriageDbContext>()
            .UseSqlServer("Server=model-only;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        _context = new ErTriageDbContext(options);
        _model = _context.GetService<IDesignTimeModel>().Model;
    }

    public void Dispose() => _context.Dispose();

    private IEntityType PatientType => _model.FindEntityType(typeof(Patient))
        ?? throw new InvalidOperationException("Patient is not mapped.");

    [Fact]
    public void Patient_IsMappedToPatientsTable_WithGuidKeyAssignedByTheDomain()
    {
        Assert.Equal("Patients", PatientType.GetTableName());

        var key = Assert.IsAssignableFrom<IKey>(PatientType.FindPrimaryKey());
        var id = Assert.Single(key.Properties);
        Assert.Equal(nameof(Patient.Id), id.Name);
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never, id.ValueGenerated);
    }

    [Fact]
    public void Patient_HasTheExpectedCheckConstraints()
    {
        var names = PatientType.GetCheckConstraints().Select(c => c.Name).ToHashSet();

        var expected = new[]
        {
            "CK_Patients_Age", "CK_Patients_HeartRate", "CK_Patients_SystolicBp", "CK_Patients_DiastolicBp",
            "CK_Patients_TemperatureC", "CK_Patients_SpO2", "CK_Patients_DiastolicBelowSystolic",
            "CK_Patients_CalculatedPriority", "CK_Patients_OverridePriority",
            "CK_Patients_OverrideFieldsConsistent", "CK_Patients_CurrentPriority", "CK_Patients_Status"
        };

        foreach (var name in expected)
        {
            Assert.Contains(name, names);
        }

        Assert.Equal(expected.Length, names.Count);
    }

    [Fact]
    public void CurrentPriorityCheck_EnforcesTheOverrideCoalesceFormula()
    {
        var check = PatientType.GetCheckConstraints().Single(c => c.Name == "CK_Patients_CurrentPriority");

        Assert.Equal("[CurrentPriority] = COALESCE([OverridePriority], [CalculatedPriority])", check.Sql);
    }

    [Fact]
    public void StatusCheck_ListsEveryPatientStatus()
    {
        var check = PatientType.GetCheckConstraints().Single(c => c.Name == "CK_Patients_Status");

        foreach (var name in Enum.GetNames<PatientStatus>())
        {
            Assert.Contains($"'{name}'", check.Sql);
        }
    }

    [Fact]
    public void VitalSigns_IsOwnedByPatient_AndStoredInTheSameTableWithUnprefixedColumns()
    {
        var vitals = _model.FindEntityType(typeof(VitalSigns))
            ?? throw new InvalidOperationException("VitalSigns is not mapped.");

        Assert.True(vitals.IsOwned());
        Assert.Equal("Patients", vitals.GetTableName());

        Assert.Equal("HeartRate", vitals.FindProperty(nameof(VitalSigns.HeartRate))!.GetColumnName());
        Assert.Equal("SystolicBp", vitals.FindProperty(nameof(VitalSigns.SystolicBp))!.GetColumnName());
        Assert.Equal("DiastolicBp", vitals.FindProperty(nameof(VitalSigns.DiastolicBp))!.GetColumnName());
        Assert.Equal("TemperatureC", vitals.FindProperty(nameof(VitalSigns.TemperatureC))!.GetColumnName());
        Assert.Equal("SpO2", vitals.FindProperty(nameof(VitalSigns.SpO2))!.GetColumnName());
        Assert.Equal("decimal(4,1)", vitals.FindProperty(nameof(VitalSigns.TemperatureC))!.GetColumnType());
    }

    [Fact]
    public void Status_IsStoredAsAShortString()
    {
        var status = PatientType.FindProperty(nameof(Patient.Status))!;

        // HasConversion<string>() sets the provider type, not an explicit converter, so assert the resulting column type.
        Assert.Equal("nvarchar(20)", status.GetColumnType());
        Assert.Equal(20, status.GetMaxLength());
        Assert.False(status.IsNullable);
    }

    [Fact]
    public void TextColumns_UseTheDomainLengthLimits()
    {
        Assert.Equal(PatientRules.FullNameMaxLength, PatientType.FindProperty(nameof(Patient.FullName))!.GetMaxLength());
        Assert.Equal(PatientRules.ChiefComplaintMaxLength, PatientType.FindProperty(nameof(Patient.ChiefComplaint))!.GetMaxLength());
        Assert.Equal(PatientRules.OverrideReasonMaxLength, PatientType.FindProperty(nameof(Patient.OverrideReason))!.GetMaxLength());
        Assert.Equal(PatientRules.OverriddenByMaxLength, PatientType.FindProperty(nameof(Patient.OverriddenBy))!.GetMaxLength());
        Assert.Equal(PatientRules.DispositionNoteMaxLength, PatientType.FindProperty(nameof(Patient.DispositionNote))!.GetMaxLength());
    }

    [Fact]
    public void OptionalOverrideAndDispositionColumns_AreNullable_AndTheRestAreRequired()
    {
        foreach (var name in new[]
                 {
                     nameof(Patient.OverridePriority), nameof(Patient.OverrideReason), nameof(Patient.OverriddenBy),
                     nameof(Patient.OverriddenAt), nameof(Patient.TreatmentStartedAt), nameof(Patient.CompletedAt),
                     nameof(Patient.DispositionNote)
                 })
        {
            Assert.True(PatientType.FindProperty(name)!.IsNullable, $"{name} should be nullable.");
        }

        foreach (var name in new[]
                 {
                     nameof(Patient.FullName), nameof(Patient.Age), nameof(Patient.ChiefComplaint),
                     nameof(Patient.ArrivalTime), nameof(Patient.CalculatedPriority), nameof(Patient.PriorityReason),
                     nameof(Patient.CurrentPriority), nameof(Patient.Status), nameof(Patient.CreatedAt)
                 })
        {
            Assert.False(PatientType.FindProperty(name)!.IsNullable, $"{name} should be required.");
        }
    }

    [Fact]
    public void RowVersion_IsTheConcurrencyToken()
    {
        var rowVersion = PatientType.FindProperty(nameof(Patient.RowVersion))!;

        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
    }

    [Fact]
    public void BoardIndex_CoversStatusPriorityArrivalAndIdInThatOrder()
    {
        var index = Assert.Single(PatientType.GetIndexes(), i => i.GetDatabaseName() == "IX_Patients_Status_CurrentPriority_ArrivalTime_Id");

        Assert.Equal(
            new[] { nameof(Patient.Status), nameof(Patient.CurrentPriority), nameof(Patient.ArrivalTime), nameof(Patient.Id) },
            index.Properties.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void IsOverridden_IsNotPersisted()
    {
        Assert.Null(PatientType.FindProperty(nameof(Patient.IsOverridden)));
    }

    [Fact]
    public void UtcConverters_RestoreUtcKindOnRead_AndKeepNullsNull()
    {
        var unspecified = new DateTime(2026, 10, 19, 12, 30, 0, DateTimeKind.Unspecified);

        var read = (DateTime)new UtcDateTimeConverter().ConvertFromProvider(unspecified)!;
        Assert.Equal(DateTimeKind.Utc, read.Kind);
        Assert.Equal(unspecified.Ticks, read.Ticks);

        var nullable = new NullableUtcDateTimeConverter();
        Assert.Equal(DateTimeKind.Utc, ((DateTime?)nullable.ConvertFromProvider(unspecified))!.Value.Kind);
        Assert.Null(nullable.ConvertFromProvider(null));
    }
}
