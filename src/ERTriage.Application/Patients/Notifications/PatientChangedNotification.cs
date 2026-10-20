using ERTriage.Domain.Patients;

namespace ERTriage.Application.Patients.Notifications;

/// <summary>
/// Minimal "something changed" signal for other clients. It deliberately carries <b>no patient data</b> (no name,
/// complaint, vitals, note or reason): receivers refetch the board through the normal API, which owns the data.
/// </summary>
public sealed record PatientChangedNotification(
    PatientChangeType ChangeType,
    Guid PatientId,
    int Priority,
    PatientStatus Status,
    DateTimeOffset OccurredAt)
{
    public static PatientChangedNotification For(PatientChangeType changeType, Patient patient, DateTimeOffset occurredAt) =>
        new(changeType, patient.Id, patient.CurrentPriority, patient.Status, occurredAt);
}
