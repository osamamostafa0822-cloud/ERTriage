using ERTriage.Api;
using ERTriage.Api.Configuration;
using ERTriage.Api.Documentation;
using ERTriage.Application;
using ERTriage.Application.Common;
using ERTriage.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApi(builder.Configuration, builder.Environment) // validates configuration first
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Development convenience (D-44, D-45): apply pending migrations once, awaited, before the first request.
// Off by default; rejected by configuration validation in Production. `dotnet ef` stops the host at Build(),
// so this never runs while generating migrations.
if (ApiConfiguration.IsMigrateOnStartupEnabled(app.Configuration))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>()
        .MigrateAsync(app.Lifetime.ApplicationStopping);
}

app.UseRequestLocalization();
app.UseExceptionHandler();
app.UseCors(ApiDependencyInjection.CorsPolicyName);

app.MapControllers();
app.MapHealthChecks("/health");
app.MapApiDocumentation(); // OpenAPI + Scalar, only when ApiDocumentation:Enabled

app.Run();
