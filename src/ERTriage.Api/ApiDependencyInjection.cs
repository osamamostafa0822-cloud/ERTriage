using System.Text.Json.Serialization;
using ERTriage.Api.Localization;
using ERTriage.Api.Configuration;
using ERTriage.Api.Documentation;
using ERTriage.Api.ExceptionHandling;
using ERTriage.Api.Realtime;
using ERTriage.Application.Patients;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ERTriage.Api;

public static class ApiDependencyInjection
{
    public const string CorsPolicyName = "Frontend";

    /// <summary>
    /// Registers API-specific services: validated configuration, controllers, error handling, CORS,
    /// health checks, OpenAPI/Scalar (when enabled) and the clock.
    /// </summary>
    public static IServiceCollection AddApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ApiConfiguration.Validate(configuration, environment);

        services
            .AddControllers()
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // Must come after AddControllers: MVC registers its own ApiBehaviorOptionsSetup there, and options setups run in
        // registration order, so an earlier InvalidModelStateResponseFactory would be overwritten by the default
        // ProblemDetails factory (which also echoes framework messages such as type names). Guarded by ApiBehaviorWiringTests.
        services.AddTranslationResources();

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails(); // required by UseExceptionHandler()

        var allowedOrigins = ApiConfiguration.GetAllowedOrigins(configuration);
        services.AddCors(options =>
            options.AddPolicy(CorsPolicyName, policy =>
                policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

        // Real-time board updates (D-79). Enums travel as strings, like the REST API. Replace (not TryAdd) so the
        // SignalR notifier wins over the Application's no-op default whatever the registration order.
        services.AddHttpContextAccessor();
        services
            .AddSignalR()
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.Replace(ServiceDescriptor.Scoped<IPatientChangeNotifier, SignalRPatientChangeNotifier>());

        services.AddHealthChecks(); // liveness only; a DB check needs an extra package (not added in M1)
        services.AddApiDocumentation(configuration);

        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
