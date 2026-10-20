namespace ERTriage.Application.Patients.Notifications;

/// <summary>Default when no real-time transport is registered (for example tests or a host without SignalR).</summary>
internal sealed class NoOpPatientChangeNotifier : IPatientChangeNotifier
{
    public Task NotifyAsync(PatientChangedNotification notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
