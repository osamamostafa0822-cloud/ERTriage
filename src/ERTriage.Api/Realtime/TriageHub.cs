using Microsoft.AspNetCore.SignalR;

namespace ERTriage.Api.Realtime;

/// <summary>
/// Server-to-client only hub (D-79): it declares no client-callable methods, so connected browsers can listen but never
/// send anything. Changes are published by <see cref="SignalRPatientChangeNotifier"/> after a successful save.
/// Demo scope: like the REST API it has no authentication; that is a documented limitation.
/// </summary>
public sealed class TriageHub : Hub
{
    public const string Path = "/hubs/triage";

    /// <summary>Client method invoked on every connected browser (payload: <c>PatientChangedNotification</c>).</summary>
    public const string PatientChangedMethod = "patientChanged";

    /// <summary>
    /// Optional request header with the caller's own SignalR connection id, so the originating browser is not told
    /// about its own change. The value is only used to exclude a connection; it is never trusted for anything else.
    /// </summary>
    public const string OriginConnectionHeader = "X-Origin-Connection-Id";
}
