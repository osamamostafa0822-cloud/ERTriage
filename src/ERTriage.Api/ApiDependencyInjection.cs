using System.Text.Json.Serialization;
using ERTriage.Api.Localization;
using ERTriage.Api.Configuration;
using ERTriage.Api.Documentation;
using ERTriage.Api.ExceptionHandling;

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
        services.AddTranslationResources();

        services
            .AddControllers()
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails(); // required by UseExceptionHandler()

        var allowedOrigins = ApiConfiguration.GetAllowedOrigins(configuration);
        services.AddCors(options =>
            options.AddPolicy(CorsPolicyName, policy =>
                policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

        services.AddHealthChecks(); // liveness only; a DB check needs an extra package (not added in M1)
        services.AddApiDocumentation(configuration);

        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
