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

// Development only (D-44, D-45, D-58): fictional demo patients, after migrations. Two independent gates: the
// environment and the flag (Validate already rejects the flag outside Development). Idempotent and lock-protected.
if (app.Environment.IsDevelopment() && ApiConfiguration.IsSeedDevelopmentDataEnabled(app.Configuration))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IDevelopmentDataSeeder>()
        .SeedAsync(app.Lifetime.ApplicationStopping);
}

app.UseRequestLocalization();
app.UseExceptionHandler();
app.UseCors(ApiDependencyInjection.CorsPolicyName);

app.MapControllers();
app.MapHealthChecks("/health");
app.MapApiDocumentation(); // OpenAPI + Scalar, only when ApiDocumentation:Enabled

app.Run();

// Makes the entry point visible to WebApplicationFactory<Program> (HTTP integration tests, M4 P4).
public partial class Program;
