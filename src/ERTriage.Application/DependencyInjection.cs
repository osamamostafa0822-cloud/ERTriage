using ERTriage.Application.Patients;
using ERTriage.Application.Triage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ERTriage.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the Application use-case services (Scoped, so they share the request's repositories and Unit of Work).
    /// <see cref="TimeProvider"/> is added with <c>TryAdd</c> so the layer works on its own and a host or test that
    /// registered another clock first (for example the Api, or a fake) keeps it.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // Real-time fan-out is optional: a host that registers a transport (the Api's SignalR notifier) keeps it.
        services.TryAddSingleton<IPatientChangeNotifier, NoOpPatientChangeNotifier>();

        services.AddScoped<IPatientRegistrationService, PatientRegistrationService>();
        services.AddScoped<ITriageBoardService, TriageBoardService>();
        services.AddScoped<IPatientWorkflowService, PatientWorkflowService>();

        return services;
    }
}
