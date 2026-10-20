using ERTriage.Domain.Patients;

namespace ERTriage.Application.Patients.Dtos;

/// <summary>
/// Public view of one patient. Never exposes the entity and never the concurrency token (D-42).
/// Timestamps are UTC. <see cref="AllowedTransitions"/> comes from the Domain so clients never copy the matrix (D-08).
/// </summary>
public sealed record PatientDto(
    Guid Id,
    string FullName,
    int Age,
    string ChiefComplaint,
    VitalSignsDto Vitals,
    DateTime ArrivalTime,
    int CalculatedPriority,
    string PriorityReason,
    int? OverridePriority,
    string? OverrideReason,
    string? OverriddenBy,
    DateTime? OverriddenAt,
    int CurrentPriority,
    bool IsOverridden,
    PatientStatus Status,
    DateTime? TreatmentStartedAt,
    DateTime? CompletedAt,
    string? DispositionNote,
    IReadOnlyList<PatientStatus> AllowedTransitions);
