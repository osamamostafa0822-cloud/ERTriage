namespace ERTriage.Infrastructure.Persistence.Seed;

/// <summary>
/// The seed document as a whole cannot be used (missing file, malformed JSON, unsupported schema, no records). Record
/// problems never raise this: they are reported per record and only that record is skipped. The message holds the
/// safe code only — no path, no JSON content.
/// </summary>
public sealed class SeedDataException : Exception
{
    public SeedDataException(string code) : base($"Development seed data file is unusable ({code}).")
    {
        Code = code;
    }

    public string Code { get; }
}
