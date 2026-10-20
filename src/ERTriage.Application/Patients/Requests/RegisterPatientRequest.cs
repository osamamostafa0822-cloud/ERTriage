namespace ERTriage.Application.Patients.Requests;

/// <summary>
/// Body of <c>POST /api/patients</c> (docs/03-features/patient-registration.md). Every member is nullable so a missing
/// field is reported as a validation error instead of silently becoming a default such as age 0.
/// </summary>
public sealed record RegisterPatientRequest(
    string? FullName,
    int? Age,
    string? ChiefComplaint,
    int? HeartRate,
    int? SystolicBp,
    int? DiastolicBp,
    decimal? TemperatureC,
    int? SpO2,
    DateTimeOffset? ArrivalTime);
