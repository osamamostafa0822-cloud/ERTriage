using ERTriage.Api;
using ERTriage.Api.Documentation;
using ERTriage.Api.Realtime;
using ERTriage.Api.Startup;
using ERTriage.Application;
using ERTriage.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApi(builder.Configuration, builder.Environment) // validates configuration first
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Migrations (when enabled) then the Development-only seed (when enabled): ERTriage.Api.Startup.
await app.InitializeDatabaseAsync();

app.UseRequestLocalization();
app.UseExceptionHandler();
app.UseCors(ApiDependencyInjection.CorsPolicyName);

app.MapControllers();
app.MapHub<TriageHub>(TriageHub.Path); // CORS: the global policy above also covers /negotiate
app.MapHealthChecks("/health");
app.MapApiDocumentation(); // OpenAPI + Scalar, only when ApiDocumentation:Enabled

app.Run();

// Makes the entry point visible to WebApplicationFactory<Program> (HTTP integration tests, M4 P4).
public partial class Program;
