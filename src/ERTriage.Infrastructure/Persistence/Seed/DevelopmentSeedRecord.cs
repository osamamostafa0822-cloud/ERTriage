namespace ERTriage.Infrastructure.Persistence.Seed;

/// <summary>
/// Marks a patient row created by the Development seed (D-58). Persistence model only, not a Domain entity.
/// <see cref="SeedKey"/> is the stable identity from the JSON file (primary key, so a key can be seeded once at a time);
/// <see cref="PatientId"/> is unique and cascades on patient delete, so a deleted fictional patient is recreated with the
/// same key on the next Development start.
/// </summary>
public sealed class DevelopmentSeedRecord
{
    // For EF Core.
    private DevelopmentSeedRecord()
    {
    }

    public DevelopmentSeedRecord(string seedKey, Guid patientId, DateTime seededAt)
    {
        SeedKey = seedKey;
        PatientId = patientId;
        SeededAt = seededAt;
    }

    public string SeedKey { get; private set; } = string.Empty;

    public Guid PatientId { get; private set; }

    public DateTime SeededAt { get; private set; }
}
