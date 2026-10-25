using ERTriage.Application.Triage.Dtos;
using ERTriage.Application.Triage.Waiting;
using ERTriage.Domain.Patients;
using ERTriage.Domain.Triage;

namespace ERTriage.Application.Triage.Mapping;

/// <summary>Maps a projected board row to its DTO. <paramref name="now"/> is read once per request by the caller.</summary>
public static class TriageBoardMapper
{
    public static TriageBoardItemDto ToItem(TriageBoardRow row, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(row);

        var waiting = WaitingTimeEvaluator.Evaluate(row.Status, row.ArrivalTime, row.TreatmentStartedAt, now);

        return new TriageBoardItemDto(
            row.Id,
            row.FullName,
            row.Age,
            row.ChiefComplaint,
            row.ArrivalTime,
            row.CurrentPriority,
            row.CalculatedPriority,
            row.PriorityReason,
            row.IsOverridden,
            row.OverrideReason,
            row.Status,
            waiting.Minutes,
            waiting.Level,
            // Same single source of truth as Patient.AllowedTransitions(); the UI never duplicates the matrix.
            PatientStatusTransitions.From(row.Status));
    }
}
