namespace ERTriage.Application.Common;

/// <summary>
/// Applies pending schema migrations (decision D-44). Migrations only: sample data is a separate concern.
/// Called once at startup, and only when <c>Database:MigrateOnStartup</c> is enabled (never in Production, D-45).
/// </summary>
public interface IDatabaseInitializer
{
    Task MigrateAsync(CancellationToken cancellationToken);
}
