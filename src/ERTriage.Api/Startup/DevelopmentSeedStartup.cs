using ERTriage.Api.Configuration;
using ERTriage.Application.Common;

namespace ERTriage.Api.Startup;

/// <summary>
/// Startup orchestration of the Development seed (D-44, D-45, D-58, D-62). Separate from <c>Program.cs</c> so the gates,
/// scoping, cancellation and result handling are unit-testable without starting a host.
/// <para>
/// No extra startup timeout: every step is already bounded (SqlClient connect timeout, EF command timeout, and the seed
/// lock timeout plus 15 s), and the host's <c>ApplicationStopping</c> token cancels the run on shutdown.
/// </para>
/// </summary>
public static class DevelopmentSeedStartup
{
    /// <summary>
    /// Both gates must hold: the Development environment <b>and</b> <c>Database:SeedDevelopmentData = true</c>.
    /// Independent of <see cref="ApiConfiguration.Validate"/>, which already rejects the flag outside Development.
    /// </summary>
    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && ApiConfiguration.IsSeedDevelopmentDataEnabled(configuration);

    /// <summary>
    /// Runs the seeder in its own DI scope (disposed asynchronously) when <see cref="IsEnabled"/>; returns <c>null</c> when
    /// a gate is closed. Cancellation and fatal errors propagate unchanged: they are never swallowed here, and the seeder
    /// has already logged a fatal error once, so nothing is logged twice.
    /// </summary>
    public static async Task<DevelopmentSeedResult?> RunIfEnabledAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled(environment, configuration))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDevelopmentDataSeeder>().SeedAsync(cancellationToken);

        return Handle(result);
    }

    /// <summary>
    /// Explicit decision per outcome. Every known outcome lets the application start: the seeder already logged the
    /// details (per-record warnings, the summary, a document error), so nothing is logged again here. A partial or
    /// failed seed only affects demo data. An unknown outcome is a programming error and stops the startup.
    /// </summary>
    public static DevelopmentSeedResult Handle(DevelopmentSeedResult result) =>
        result.Outcome switch
        {
            DevelopmentSeedOutcome.Completed => result,
            DevelopmentSeedOutcome.AlreadyComplete => result,
            DevelopmentSeedOutcome.PartiallyCompleted => result, // record-level problems: retried on next start
            DevelopmentSeedOutcome.LockNotAcquired => result,    // another instance is seeding the same data
            DevelopmentSeedOutcome.Failed => result,             // document-level (DocumentErrorCode) or every record invalid
            _ => throw new InvalidOperationException($"Unhandled development seed outcome '{result.Outcome}'.")
        };
}
