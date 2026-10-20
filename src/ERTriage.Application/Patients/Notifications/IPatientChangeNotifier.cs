namespace ERTriage.Application.Patients.Notifications;

/// <summary>
/// Best-effort real-time fan-out of committed patient changes (D-79). Called by the use-case services only after
/// <c>SaveChangesAsync</c> succeeded. Implementations must not throw for delivery failures: a lost notification must
/// never turn a committed change into an HTTP error; clients still converge through the next board fetch.
/// </summary>
public interface IPatientChangeNotifier
{
    Task NotifyAsync(PatientChangedNotification notification, CancellationToken cancellationToken);
}
