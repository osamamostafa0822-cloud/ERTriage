namespace ERTriage.Infrastructure.Persistence.Seed;

/// <summary>Stable, safe error codes for seed problems. Logged instead of messages, which could carry values.</summary>
internal static class SeedErrorCodes
{
    // Document level (the whole file is rejected; nothing is written)
    public const string FileNotFound = "FileNotFound";
    public const string MalformedDocument = "MalformedDocument";
    public const string EmptyDocument = "EmptyDocument";
    public const string UnsupportedSchemaVersion = "UnsupportedSchemaVersion";
    public const string NoRecords = "NoRecords";
    public const string TooManyRecords = "TooManyRecords";

    // Record level (only that record is skipped)
    public const string NullRecord = "NullRecord";
    public const string InvalidJson = "InvalidJson";
    public const string Required = "Required";
    public const string OutOfRange = "OutOfRange";
    public const string InvalidLength = "InvalidLength";
    public const string InvalidFormat = "InvalidFormat";
    public const string NotFictionalName = "NotFictionalName";
    public const string DuplicateSeedKey = "DuplicateSeedKey";
    public const string PrecisionExceeded = "PrecisionExceeded";
    public const string DiastolicNotBelowSystolic = "DiastolicNotBelowSystolic";
    public const string RequiredForStatus = "RequiredForStatus";
    public const string NotAllowedForStatus = "NotAllowedForStatus";
    public const string TransitionNotAllowed = "TransitionNotAllowed";
    public const string TimelineOrder = "TimelineOrder";
    public const string InFuture = "InFuture";
    public const string OverrideAfterCompletion = "OverrideAfterCompletion";

    // Record level, after validation (the record failed; retried next start)
    public const string DomainRuleViolation = "DomainRuleViolation";
    public const string DatabaseConstraint = "DatabaseConstraint";
}
