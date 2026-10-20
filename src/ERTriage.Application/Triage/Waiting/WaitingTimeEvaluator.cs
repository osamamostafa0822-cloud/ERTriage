using ERTriage.Domain.Patients;

namespace ERTriage.Application.Triage.Waiting;

/// <summary>Result of the waiting-time rule: whole minutes (null when there is no indicator) and the level.</summary>
public sealed record WaitingTime(int? Minutes, WaitingLevel Level);

/// <summary>
/// Pure waiting-time rule (docs/03-features/waiting-time.md): no I/O and no clock of its own; the caller passes
/// <c>now</c> from the injected <see cref="TimeProvider"/> (D-07). Thresholds are fixed, priority-independent and
/// non-clinical.
/// </summary>
public static class WaitingTimeEvaluator
{
    public const int WarningMinutes = 30;
    public const int CriticalMinutes = 60;

    public static WaitingTime Evaluate(Patient patient, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(patient);

        switch (patient.Status)
        {
            case PatientStatus.Waiting:
                var minutes = WholeMinutes(patient.ArrivalTime, now.UtcDateTime);
                return new WaitingTime(minutes, LevelFor(minutes));

            case PatientStatus.InTreatment when patient.TreatmentStartedAt is { } startedAt:
                // Frozen at the moment treatment began: "Waited N min", no indicator level.
                return new WaitingTime(WholeMinutes(patient.ArrivalTime, startedAt), WaitingLevel.None);

            default:
                return new WaitingTime(null, WaitingLevel.None);
        }
    }

    private static WaitingLevel LevelFor(int minutes) =>
        minutes >= CriticalMinutes ? WaitingLevel.Critical
        : minutes >= WarningMinutes ? WaitingLevel.Warning
        : WaitingLevel.Normal;

    // Floor to whole minutes; an arrival slightly in the future (within the registration tolerance) counts as 0.
    private static int WholeMinutes(DateTime from, DateTime to)
    {
        var minutes = Math.Floor((to - from).TotalMinutes);

        return minutes <= 0 ? 0 : minutes >= int.MaxValue ? int.MaxValue : (int)minutes;
    }
}
