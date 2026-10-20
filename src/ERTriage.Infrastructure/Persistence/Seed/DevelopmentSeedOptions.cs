namespace ERTriage.Infrastructure.Persistence.Seed;

/// <summary>Technical settings of the Development seeder. Defaults are used by the application; tests override them.</summary>
public sealed class DevelopmentSeedOptions
{
    /// <summary>How long to wait for the seed lock held by another starting instance before skipping.</summary>
    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Absolute path of the JSON file. Default: the copy in the application's output folder.</summary>
    public string DataFilePath { get; init; } = FictionalPatientSeedLoader.DefaultPath;
}
