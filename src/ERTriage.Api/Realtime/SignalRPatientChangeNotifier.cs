using ERTriage.Application.Patients;
using Microsoft.AspNetCore.SignalR;

namespace ERTriage.Api.Realtime;

/// <summary>
/// Publishes <see cref="PatientChangedNotification"/> to every connected board except the request's own connection.
/// Delivery is best effort: a failure is logged once (no patient data) and never reaches the caller.
/// </summary>
public sealed class SignalRPatientChangeNotifier(
    IHubContext<TriageHub> hub,
    IHttpContextAccessor httpContextAccessor,
    ILogger<SignalRPatientChangeNotifier> logger) : IPatientChangeNotifier
{
    private const int MaxConnectionIdLength = 128;

    public async Task NotifyAsync(PatientChangedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var origin = httpContextAccessor.HttpContext?.Request.Headers[TriageHub.OriginConnectionHeader].ToString();
            var clients = string.IsNullOrWhiteSpace(origin) || origin.Length > MaxConnectionIdLength
                ? hub.Clients.All
                : hub.Clients.AllExcept(origin);

            await clients.SendAsync(TriageHub.PatientChangedMethod, notification, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Patient change notification failed. PatientId {PatientId}, ChangeType {ChangeType}",
                notification.PatientId,
                notification.ChangeType);
        }
    }
}
