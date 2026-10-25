using ERTriage.Domain.Patients;

namespace ERTriage.Domain.Triage;

/// <summary>
/// Read model of one board row: only the columns the board needs, projected by the database (no vitals, disposition
/// note, audit timestamps or concurrency token). Not a public DTO: Application maps it to the board DTO and adds the
/// waiting indicator and allowed transitions.
/// </summary>
public sealed record TriageBoardRow(
    Guid Id,
    string FullName,
    int Age,
    string ChiefComplaint,
    DateTime ArrivalTime,
    int CurrentPriority,
    int CalculatedPriority,
    string PriorityReason,
    bool IsOverridden,
    string? OverrideReason,
    PatientStatus Status,
    DateTime? TreatmentStartedAt);
