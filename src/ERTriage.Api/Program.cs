using ERTriage.Api;
using ERTriage.Api.Documentation;
using ERTriage.Application;
using ERTriage.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApi(builder.Configuration, builder.Environment) // validates configuration first
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors(ApiDependencyInjection.CorsPolicyName);

app.MapControllers();
app.MapHealthChecks("/health");
app.MapApiDocumentation(); // OpenAPI + Scalar, only when ApiDocumentation:Enabled

app.Run();
