using ERTriage.Domain.Patients;

namespace ERTriage.Application.Patients;

/// <summary>What happened to a patient. Wire values are the enum names (the Api serializes enums as strings).</summary>
public enum PatientChangeType
{
    Registered,
    StatusChanged,
    PriorityOverridden
}

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

/// <summary>
/// Best-effort real-time fan-out of committed patient changes (D-79). Called by the use-case services only after
/// <c>SaveChangesAsync</c> succeeded. Implementations must not throw for delivery failures: a lost notification must
/// never turn a committed change into an HTTP error; clients still converge through the next board fetch.
/// </summary>
public interface IPatientChangeNotifier
{
    Task NotifyAsync(PatientChangedNotification notification, CancellationToken cancellationToken);
}

/// <summary>Default when no real-time transport is registered (for example tests or a host without SignalR).</summary>
internal sealed class NoOpPatientChangeNotifier : IPatientChangeNotifier
{
    public Task NotifyAsync(PatientChangedNotification notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
