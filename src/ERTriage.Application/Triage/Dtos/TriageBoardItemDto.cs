using ERTriage.Domain.Patients;
using ERTriage.Application.Triage.Waiting;

namespace ERTriage.Application.Triage.Dtos;

/// <summary>One row of the triage board (docs/03-features/triage-board.md). No concurrency token (D-42).</summary>
public sealed record TriageBoardItemDto(
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
    int? WaitingMinutes,
    WaitingLevel WaitingLevel,
    IReadOnlyList<PatientStatus> AllowedTransitions);
