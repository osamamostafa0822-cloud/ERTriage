using System.Text.Json;
using System.Text.Json.Serialization;

namespace ERTriage.Infrastructure.Persistence.Seed;

/// <summary>
/// Reads <c>Data/fictional-patients.json</c> with System.Text.Json (no extra package). The document is parsed first;
/// each record is then deserialized and validated on its own (<see cref="FictionalPatientSeedValidator"/>), so one bad
/// record cannot hide the others. A document that cannot be parsed reliably throws <see cref="SeedDataException"/>.
/// </summary>
internal static class FictionalPatientSeedLoader
{
    /// <summary>Path of the data file relative to the application's output folder (copied on build and publish).</summary>
    public static readonly string RelativePath = Path.Combine("Persistence", "Seed", "Data", "fictional-patients.json");

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // A misspelled property must be reported instead of being ignored.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, RelativePath);

    public static async Task<SeedLoadResult> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new SeedDataException(SeedErrorCodes.FileNotFound);
        }

        await using var stream = File.OpenRead(path);
        FictionalPatientSeedFile? file;

        try
        {
            file = await JsonSerializer.DeserializeAsync<FictionalPatientSeedFile>(stream, Options, cancellationToken);
        }
        catch (JsonException)
        {
            throw new SeedDataException(SeedErrorCodes.MalformedDocument);
        }

        return FictionalPatientSeedValidator.Validate(file, Options);
    }

    /// <summary>Parses and validates seed JSON held in memory (tests).</summary>
    public static SeedLoadResult Parse(string json)
    {
        FictionalPatientSeedFile? file;

        try
        {
            file = JsonSerializer.Deserialize<FictionalPatientSeedFile>(json, Options);
        }
        catch (JsonException)
        {
            throw new SeedDataException(SeedErrorCodes.MalformedDocument);
        }

        return FictionalPatientSeedValidator.Validate(file, Options);
    }
}
