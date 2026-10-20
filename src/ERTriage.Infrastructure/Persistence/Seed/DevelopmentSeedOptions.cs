namespace ERTriage.Infrastructure.Persistence.Seed;

/// <summary>Technical settings of the Development seeder. Defaults are used by the application; tests override them.</summary>
public sealed class DevelopmentSeedOptions
{
    /// <summary>How long to wait for the seed lock held by another starting instance before skipping.</summary>
    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Records saved per <c>SaveChanges</c> (each batch has its own savepoint). A batch that hits a recoverable error is
    /// rolled back and retried record by record, so only the failing record is lost (D-62, D-63).
    /// </summary>
    public int BatchSize { get; init; } = 200;

    /// <summary>Absolute path of the JSON file. Default: the copy in the application's output folder.</summary>
    public string DataFilePath { get; init; } = FictionalPatientSeedLoader.DefaultPath;
}
