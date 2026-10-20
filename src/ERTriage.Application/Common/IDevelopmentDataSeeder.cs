namespace ERTriage.Application.Common;

/// <summary>
/// Inserts the fictional demo patients into a Development database (D-44, D-58, D-62). Called once from
/// <c>Program.cs</c> after migrations and only when the host is Development and <c>Database:SeedDevelopmentData</c> is
/// true. Best-effort per record: an invalid or failing record is skipped and retried on the next start; fatal
/// database/infrastructure errors are logged and rethrown. Implemented in Infrastructure.
/// </summary>
public interface IDevelopmentDataSeeder
{
    Task<DevelopmentSeedResult> SeedAsync(CancellationToken cancellationToken);
}

/// <summary>What one seed run did. Counts only, never patient data.</summary>
/// <param name="SeedRunId">Correlates the run's log entries.</param>
/// <param name="TotalRecords">Records in the seed file (0 when the document itself is unusable).</param>
/// <param name="Inserted">Patients inserted by this run.</param>
/// <param name="AlreadyExisting">Records whose seed key was already present (never modified).</param>
/// <param name="Skipped">Records rejected by validation (invalid data, duplicate seed key); retried next start.</param>
/// <param name="Failed">Valid records that failed in the Domain or with a recoverable database error; retried next start.</param>
public sealed record DevelopmentSeedResult(
    Guid SeedRunId,
    DevelopmentSeedOutcome Outcome,
    int TotalRecords,
    int Inserted,
    int AlreadyExisting,
    int Skipped,
    int Failed);

public enum DevelopmentSeedOutcome
{
    /// <summary>Every record is now seeded and this run inserted at least one patient.</summary>
    Completed,

    /// <summary>Some records were skipped or failed; the others were inserted or already present. The demo board may be incomplete.</summary>
    PartiallyCompleted,

    /// <summary>Every record was already present; nothing was written.</summary>
    AlreadyComplete,

    /// <summary>Another instance held the seed lock longer than the timeout; nothing was written by this run.</summary>
    LockNotAcquired,

    /// <summary>The document was unusable (missing, malformed, wrong schema) or no record could be seeded; nothing was written.</summary>
    Failed
}
