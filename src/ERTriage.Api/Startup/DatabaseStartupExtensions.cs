using ERTriage.Api.Configuration;
using ERTriage.Application.Common;

namespace ERTriage.Api.Startup;

/// <summary>Database work done once at startup, after <c>Build()</c> and before <c>Run()</c> (D-44, D-45).</summary>
public static class DatabaseStartupExtensions
{
    /// <summary>
    /// 1. Apply pending migrations when <c>Database:MigrateOnStartup</c> is true (rejected in Production by validation).
    /// 2. Seed fictional Development data when the environment is Development and <c>Database:SeedDevelopmentData</c> is true.
    /// Both are awaited and cancelled by <c>ApplicationStopping</c>. <c>dotnet ef</c> stops the host at <c>Build()</c>, so
    /// neither runs while generating migrations.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        var cancellationToken = app.Lifetime.ApplicationStopping;

        if (ApiConfiguration.IsMigrateOnStartupEnabled(app.Configuration))
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>().MigrateAsync(cancellationToken);
        }

        await DevelopmentSeedStartup.RunIfEnabledAsync(app.Services, app.Environment, app.Configuration, cancellationToken);
    }
}
