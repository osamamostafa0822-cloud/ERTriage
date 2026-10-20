using System.Text.Json;
using System.Text.RegularExpressions;
using ERTriage.Domain.Patients;
using ERTriage.Domain.Triage;

namespace ERTriage.Infrastructure.Persistence.Seed;

/// <summary>
/// Validates the seed document (D-60, D-62). Document problems (unsupported schema, no records) throw
/// <see cref="SeedDataException"/>; every record is validated <b>independently</b> and an invalid record is reported as
/// <see cref="SeedRecordIssue"/>s and skipped, without affecting the others. Limits come from the Domain constants and
/// the transition matrix from <see cref="PatientStatusTransitions"/>; the Domain enforces every rule again on creation.
/// </summary>
internal static partial class FictionalPatientSeedValidator
{
    public const int SupportedSchemaVersion = 1;
    public const int SeedKeyMaxLength = 50;
    public const int MaxPatients = 100;

    /// <summary>Arrival can be at most this long ago (keeps demo data recent and offsets small).</summary>
    public const int MaxArrivedMinutesAgo = 24 * 60;

    /// <summary>Every seed name starts with this prefix, so fictional rows are obvious to anyone reading the board.</summary>
    public const string FictionalNamePrefix = "Test Patient ";

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SeedKeyPattern();

    /// <summary>True when <paramref name="key"/> is a well-formed seed key (safe to log and to store).</summary>
    public static bool IsWellFormedKey(string? key) =>
        !string.IsNullOrEmpty(key) && key.Length <= SeedKeyMaxLength && SeedKeyPattern().IsMatch(key);

    public static SeedLoadResult Validate(FictionalPatientSeedFile? file, JsonSerializerOptions recordOptions)
    {
        if (file is null)
        {
            throw new SeedDataException(SeedErrorCodes.EmptyDocument);
        }

        if (file.SchemaVersion != SupportedSchemaVersion)
        {
            throw new SeedDataException(SeedErrorCodes.UnsupportedSchemaVersion);
        }

        if (file.Patients is not { Count: > 0 } records)
        {
            throw new SeedDataException(SeedErrorCodes.NoRecords);
        }

        if (records.Count > MaxPatients)
        {
            throw new SeedDataException(SeedErrorCodes.TooManyRecords);
        }

        var issues = new List<SeedRecordIssue>();
        var candidates = new List<ValidatedFictionalPatient>();

        for (var index = 0; index < records.Count; index++)
        {
            var validated = ValidateRecord(index, records[index], recordOptions, issues);
            if (validated is not null)
            {
                candidates.Add(validated);
            }
        }

        // Duplicate keys: every record sharing a key is skipped (deterministic; no guess about which copy is right).
        var duplicateKeys = KeysOf(records, recordOptions)
            .Where(k => k.Key is not null)
            .GroupBy(k => k.Key!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .ToList();

        foreach (var (index, key) in duplicateKeys)
        {
            issues.Add(new SeedRecordIssue(index, key, SeedErrorCodes.DuplicateSeedKey, "seedKey"));
        }

        var skippedIndexes = issues.Select(i => i.Index).ToHashSet();
        var valid = candidates.Where(c => !skippedIndexes.Contains(c.Index)).ToList();

        return new SeedLoadResult(records.Count, valid, issues.OrderBy(i => i.Index).ToList());
    }

    /// <summary>The status changes, in order, that take a newly registered (Waiting) patient to <paramref name="target"/>.</summary>
    internal static IReadOnlyList<PatientStatus> PathTo(PatientStatus target) =>
        target switch
        {
            PatientStatus.Waiting => [],
            PatientStatus.InTreatment => [PatientStatus.InTreatment],
            _ => [PatientStatus.InTreatment, target]
        };

    private static IEnumerable<(int Index, string? Key)> KeysOf(IReadOnlyList<JsonElement> records, JsonSerializerOptions options)
    {
        for (var index = 0; index < records.Count; index++)
        {
            if (records[index].ValueKind == JsonValueKind.Object
                && records[index].TryGetProperty(options.PropertyNamingPolicy?.ConvertName("SeedKey") ?? "seedKey", out var key)
                && key.ValueKind == JsonValueKind.String
                && IsWellFormedKey(key.GetString()))
            {
                yield return (index, key.GetString());
            }
        }
    }

    private static ValidatedFictionalPatient? ValidateRecord(
        int index,
        JsonElement element,
        JsonSerializerOptions options,
        List<SeedRecordIssue> issues)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            issues.Add(new SeedRecordIssue(index, null, SeedErrorCodes.NullRecord, "$"));
            return null;
        }

        FictionalPatientSeed? seed;
        try
        {
            seed = element.Deserialize<FictionalPatientSeed>(options);
        }
        catch (JsonException ex)
        {
            issues.Add(new SeedRecordIssue(index, SafeKey(element, options), SeedErrorCodes.InvalidJson, FieldFromPath(ex.Path)));
            return null;
        }

        if (seed is null)
        {
            issues.Add(new SeedRecordIssue(index, null, SeedErrorCodes.NullRecord, "$"));
            return null;
        }

        var key = IsWellFormedKey(seed.SeedKey) ? seed.SeedKey : null;
        var before = issues.Count;
        void Fail(string code, string field) => issues.Add(new SeedRecordIssue(index, key, code, field));

        // Identity
        if (string.IsNullOrWhiteSpace(seed.SeedKey))
        {
            Fail(SeedErrorCodes.Required, "seedKey");
        }
        else if (key is null)
        {
            Fail(SeedErrorCodes.InvalidFormat, "seedKey");
        }

        // Demographics
        var name = seed.FullName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            Fail(SeedErrorCodes.Required, "fullName");
        }
        else if (name.Length < PatientRules.FullNameMinLength || name.Length > PatientRules.FullNameMaxLength)
        {
            Fail(SeedErrorCodes.InvalidLength, "fullName");
        }
        else if (!name.StartsWith(FictionalNamePrefix, StringComparison.Ordinal))
        {
            Fail(SeedErrorCodes.NotFictionalName, "fullName");
        }

        RequireRange(seed.Age, "age", PatientRules.AgeMin, PatientRules.AgeMax, Fail);

        var complaint = seed.ChiefComplaint?.Trim();
        if (string.IsNullOrEmpty(complaint))
        {
            Fail(SeedErrorCodes.Required, "chiefComplaint");
        }
        else if (complaint.Length < PatientRules.ChiefComplaintMinLength || complaint.Length > PatientRules.ChiefComplaintMaxLength)
        {
            Fail(SeedErrorCodes.InvalidLength, "chiefComplaint");
        }

        // Vital signs (validation ranges, not triage thresholds)
        var vitals = seed.Vitals;
        if (vitals is null)
        {
            Fail(SeedErrorCodes.Required, "vitals");
        }
        else
        {
            RequireRange(vitals.HeartRate, "vitals.heartRate", VitalSignsLimits.HeartRateMin, VitalSignsLimits.HeartRateMax, Fail);
            var sbpOk = RequireRange(vitals.SystolicBp, "vitals.systolicBp", VitalSignsLimits.SystolicBpMin, VitalSignsLimits.SystolicBpMax, Fail);
            var dbpOk = RequireRange(vitals.DiastolicBp, "vitals.diastolicBp", VitalSignsLimits.DiastolicBpMin, VitalSignsLimits.DiastolicBpMax, Fail);
            if (sbpOk && dbpOk && vitals.DiastolicBp >= vitals.SystolicBp)
            {
                Fail(SeedErrorCodes.DiastolicNotBelowSystolic, "vitals.diastolicBp");
            }

            if (vitals.TemperatureC is not { } temperature)
            {
                Fail(SeedErrorCodes.Required, "vitals.temperatureC");
            }
            else if (temperature < VitalSignsLimits.TemperatureCMin || temperature > VitalSignsLimits.TemperatureCMax)
            {
                Fail(SeedErrorCodes.OutOfRange, "vitals.temperatureC");
            }
            else if (decimal.Round(temperature, 1) != temperature)
            {
                Fail(SeedErrorCodes.PrecisionExceeded, "vitals.temperatureC");
            }

            RequireRange(vitals.SpO2, "vitals.spO2", VitalSignsLimits.SpO2Min, VitalSignsLimits.SpO2Max, Fail);
        }

        // Timeline
        var arrivedOk = RequireRange(seed.ArrivedMinutesAgo, "arrivedMinutesAgo", 0, MaxArrivedMinutesAgo, Fail);
        var arrivedAgo = seed.ArrivedMinutesAgo ?? 0;

        PatientStatus? status = null;
        if (string.IsNullOrWhiteSpace(seed.Status))
        {
            Fail(SeedErrorCodes.Required, "status");
        }
        else if (Enum.TryParse<PatientStatus>(seed.Status, ignoreCase: false, out var parsed)
                 && Enum.IsDefined(parsed)
                 && !int.TryParse(seed.Status, out _))
        {
            status = parsed;
        }
        else
        {
            Fail(SeedErrorCodes.InvalidFormat, "status");
        }

        var treatment = seed.TreatmentStartedMinutesAfterArrival;
        var completed = seed.CompletedMinutesAfterArrival;
        var note = seed.DispositionNote?.Trim();

        if (status is { } target)
        {
            // Every step from Waiting to the target must be allowed by the Domain's transition matrix.
            var current = PatientStatus.Waiting;
            foreach (var step in PathTo(target))
            {
                if (!PatientStatusTransitions.IsAllowed(current, step))
                {
                    Fail(SeedErrorCodes.TransitionNotAllowed, "status");
                    break;
                }

                current = step;
            }

            var needsTreatment = target != PatientStatus.Waiting;
            var needsCompletion = target is PatientStatus.Discharged or PatientStatus.Admitted;

            CheckPresence(treatment, needsTreatment, "treatmentStartedMinutesAfterArrival", Fail);
            CheckPresence(completed, needsCompletion, "completedMinutesAfterArrival", Fail);

            if (needsCompletion)
            {
                if (string.IsNullOrEmpty(note))
                {
                    Fail(SeedErrorCodes.RequiredForStatus, "dispositionNote");
                }
                else if (note.Length < PatientRules.DispositionNoteMinLength || note.Length > PatientRules.DispositionNoteMaxLength)
                {
                    Fail(SeedErrorCodes.InvalidLength, "dispositionNote");
                }
            }
            else if (seed.DispositionNote is not null)
            {
                Fail(SeedErrorCodes.NotAllowedForStatus, "dispositionNote");
            }

            if (arrivedOk)
            {
                CheckOffset(treatment, "treatmentStartedMinutesAfterArrival", arrivedAgo, Fail);
                CheckOffset(completed, "completedMinutesAfterArrival", arrivedAgo, Fail);
            }

            if (treatment is { } t && completed is { } c && c < t)
            {
                Fail(SeedErrorCodes.TimelineOrder, "completedMinutesAfterArrival");
            }
        }

        // Optional priority override
        ValidatedOverride? validatedOverride = null;
        if (seed.PriorityOverride is { } priorityOverride)
        {
            var overrideBefore = issues.Count;

            if (priorityOverride.Priority is not { } priority)
            {
                Fail(SeedErrorCodes.Required, "priorityOverride.priority");
            }
            else if (!TriagePriority.IsValid(priority))
            {
                Fail(SeedErrorCodes.OutOfRange, "priorityOverride.priority");
            }

            var reason = priorityOverride.Reason?.Trim();
            if (string.IsNullOrEmpty(reason))
            {
                Fail(SeedErrorCodes.Required, "priorityOverride.reason");
            }
            else if (reason.Length < PatientRules.OverrideReasonMinLength || reason.Length > PatientRules.OverrideReasonMaxLength)
            {
                Fail(SeedErrorCodes.InvalidLength, "priorityOverride.reason");
            }

            var overriddenBy = priorityOverride.OverriddenBy?.Trim();
            if (overriddenBy is { Length: > PatientRules.OverriddenByMaxLength })
            {
                Fail(SeedErrorCodes.InvalidLength, "priorityOverride.overriddenBy");
            }

            if (priorityOverride.MinutesAfterArrival is not { } overrideAt)
            {
                Fail(SeedErrorCodes.Required, "priorityOverride.minutesAfterArrival");
            }
            else
            {
                if (arrivedOk)
                {
                    CheckOffset(overrideAt, "priorityOverride.minutesAfterArrival", arrivedAgo, Fail);
                }

                // The Domain rejects overrides once a patient is Discharged/Admitted.
                if (completed is { } completedAt && overrideAt > completedAt)
                {
                    Fail(SeedErrorCodes.OverrideAfterCompletion, "priorityOverride.minutesAfterArrival");
                }
            }

            if (issues.Count == overrideBefore)
            {
                validatedOverride = new ValidatedOverride(
                    priorityOverride.Priority!.Value,
                    reason!,
                    string.IsNullOrEmpty(overriddenBy) ? null : overriddenBy,
                    priorityOverride.MinutesAfterArrival!.Value);
            }
        }

        if (issues.Count > before || vitals is null || status is null || key is null)
        {
            return null;
        }

        return new ValidatedFictionalPatient(
            index,
            key,
            name!,
            seed.Age!.Value,
            complaint!,
            vitals.HeartRate!.Value,
            vitals.SystolicBp!.Value,
            vitals.DiastolicBp!.Value,
            vitals.TemperatureC!.Value,
            vitals.SpO2!.Value,
            arrivedAgo,
            status.Value,
            treatment,
            completed,
            string.IsNullOrEmpty(note) ? null : note,
            validatedOverride);
    }

    private static bool RequireRange(int? value, string field, int min, int max, Action<string, string> fail)
    {
        if (value is not { } number)
        {
            fail(SeedErrorCodes.Required, field);
            return false;
        }

        if (number < min || number > max)
        {
            fail(SeedErrorCodes.OutOfRange, field);
            return false;
        }

        return true;
    }

    private static void CheckPresence(int? value, bool required, string field, Action<string, string> fail)
    {
        if (required && value is null)
        {
            fail(SeedErrorCodes.RequiredForStatus, field);
        }
        else if (!required && value is not null)
        {
            fail(SeedErrorCodes.NotAllowedForStatus, field);
        }
    }

    /// <summary>An event offset must lie between arrival and "now": 0 ≤ offset ≤ arrivedMinutesAgo.</summary>
    private static void CheckOffset(int? offset, string field, int arrivedMinutesAgo, Action<string, string> fail)
    {
        if (offset is { } minutes && (minutes < 0 || minutes > arrivedMinutesAgo))
        {
            fail(SeedErrorCodes.InFuture, field);
        }
    }

    /// <summary>The key of a record that could not be deserialized, but only when it is well-formed (safe to log).</summary>
    private static string? SafeKey(JsonElement element, JsonSerializerOptions options) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(options.PropertyNamingPolicy?.ConvertName("SeedKey") ?? "seedKey", out var key)
        && key.ValueKind == JsonValueKind.String
        && IsWellFormedKey(key.GetString())
            ? key.GetString()
            : null;

    /// <summary>"$.vitals.heartRate" → "vitals.heartRate". A JSON path names a field, never a value.</summary>
    private static string FieldFromPath(string? path) =>
        string.IsNullOrEmpty(path) || path == "$" ? "$" : path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path;
}
