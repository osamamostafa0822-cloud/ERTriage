using ERTriage.Api.Configuration;
using Scalar.AspNetCore;

namespace ERTriage.Api.Documentation;

/// <summary>
/// OpenAPI document (ASP.NET Core built-in) + Scalar UI. Entirely gated by <c>ApiDocumentation:Enabled</c>:
/// true in Development/Test, false in Production unless explicitly overridden.
/// </summary>
public static class ApiDocumentationExtensions
{
    public const string ScalarPath = "/scalar";
    private const string Title = "ER Triage Board API";

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services, IConfiguration configuration)
    {
        if (!ApiConfiguration.IsApiDocumentationEnabled(configuration))
        {
            return services;
        }

        services.AddOpenApi(options =>
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                if (document.Info is { } info)
                {
                    info.Title = Title;
                    info.Version = "v1";
                    info.Description =
                        "Demo ER triage board. Fictional data only; priority rules are illustrative and not clinically validated. " +
                        "All JSON responses use the GeneralResponse<T> envelope.";
                }

                return Task.CompletedTask;
            }));

        return services;
    }

    public static WebApplication MapApiDocumentation(this WebApplication app)
    {
        if (!ApiConfiguration.IsApiDocumentationEnabled(app.Configuration))
        {
            return app;
        }

        app.MapOpenApi(); // /openapi/v1.json
        app.MapScalarApiReference(ScalarPath, options => options.WithTitle(Title));

        return app;
    }
}
