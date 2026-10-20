using ERTriage.Application.Triage.Dtos;
using ERTriage.Domain.Patients;
using ERTriage.Application.Triage.Waiting;

namespace ERTriage.Application.Triage.Mapping;

/// <summary>Maps a patient to a board row. <paramref name="now"/> is read once per request by the caller.</summary>
public static class TriageBoardMapper
{
    public static TriageBoardItemDto ToItem(Patient patient, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(patient);

        var waiting = WaitingTimeEvaluator.Evaluate(patient, now);

        return new TriageBoardItemDto(
            patient.Id,
            patient.FullName,
            patient.Age,
            patient.ChiefComplaint,
            patient.ArrivalTime,
            patient.CurrentPriority,
            patient.CalculatedPriority,
            patient.PriorityReason,
            patient.IsOverridden,
            patient.OverrideReason,
            patient.Status,
            waiting.Minutes,
            waiting.Level,
            patient.AllowedTransitions());
    }
}
