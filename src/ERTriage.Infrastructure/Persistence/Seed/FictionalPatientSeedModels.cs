using System.Text.Json;
using ERTriage.Domain.Patients;

namespace ERTriage.Infrastructure.Persistence.Seed;

// ---- Raw JSON contract (System.Text.Json). Every member is nullable so a missing value is reported by the validator
// ---- with the record's seed key, instead of silently becoming a default such as age 0.

/// <summary>
/// Root of <c>Data/fictional-patients.json</c>. Records are kept as raw elements so each one is deserialized and
/// validated on its own: a broken record is skipped without losing the others (best-effort, D-62).
/// </summary>
internal sealed record FictionalPatientSeedFile(int? SchemaVersion, IReadOnlyList<JsonElement>? Patients);

/// <summary>One fictional patient as written in the JSON file. Times are relative to "now" at seed time.</summary>
internal sealed record FictionalPatientSeed(
    string? SeedKey,
    string? FullName,
    int? Age,
    string? ChiefComplaint,
    FictionalVitalsSeed? Vitals,
    int? ArrivedMinutesAgo,
    string? Status,
    int? TreatmentStartedMinutesAfterArrival,
    int? CompletedMinutesAfterArrival,
    string? DispositionNote,
    FictionalOverrideSeed? PriorityOverride);

internal sealed record FictionalVitalsSeed(int? HeartRate, int? SystolicBp, int? DiastolicBp, decimal? TemperatureC, int? SpO2);

internal sealed record FictionalOverrideSeed(int? Priority, string? Reason, string? OverriddenBy, int? MinutesAfterArrival);

// ---- Validated model: produced only by FictionalPatientSeedValidator, so the factory never sees nulls.

internal sealed record ValidatedFictionalPatient(
    int Index,
    string SeedKey,
    string FullName,
    int Age,
    string ChiefComplaint,
    int HeartRate,
    int SystolicBp,
    int DiastolicBp,
    decimal TemperatureC,
    int SpO2,
    int ArrivedMinutesAgo,
    PatientStatus Status,
    int? TreatmentStartedMinutesAfterArrival,
    int? CompletedMinutesAfterArrival,
    string? DispositionNote,
    ValidatedOverride? PriorityOverride);

internal sealed record ValidatedOverride(int Priority, string Reason, string? OverriddenBy, int MinutesAfterArrival);

// ---- Validation outcome.

/// <summary>One problem of one record: array index, seed key (only when it is well-formed), safe code and field.</summary>
internal sealed record SeedRecordIssue(int Index, string? SeedKey, string Code, string Field);

/// <summary>Result of loading the file: records ready to seed, and the problems of the skipped ones.</summary>
internal sealed record SeedLoadResult(
    int TotalRecords,
    IReadOnlyList<ValidatedFictionalPatient> Valid,
    IReadOnlyList<SeedRecordIssue> Issues)
{
    /// <summary>Number of records skipped (a record with several problems counts once).</summary>
    public int SkippedCount => Issues.Select(i => i.Index).Distinct().Count();
}
