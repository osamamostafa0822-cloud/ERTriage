using System.Text.RegularExpressions;

namespace ERTriage.Api.IntegrationTests.Persistence;

/// <summary>
/// Pure rules of the SQL Server integration tests (decision D-47): when they are mandatory, when they may be
/// skipped, and which database names the fixture may create and drop. No I/O, so they are unit-tested without a server.
/// </summary>
internal static class SqlServerTestPolicy
{
    public const string RequireVariable = "ERTRIAGE_REQUIRE_SQLSERVER";
    public const string ServerVariable = "ERTRIAGE_TEST_SQLSERVER";
    public const string ExperimentVariable = "ERTRIAGE_RUN_D38";
    public const string DatabasePrefix = "ERTriage_Test_";

    // \z (not $) so a trailing newline cannot slip through. Lower-case hex only: exactly what NewDatabaseName produces.
    // The Development database ("ERTriage") and the API's Test database ("ERTriage_Test") can never match.
    private static readonly Regex SafeDatabaseName =
        new(@"^ERTriage_Test_[0-9a-f]{8}\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// SQL Server tests are mandatory when the explicit flag is set or when running in CI (GitHub Actions or a
    /// generic CI variable), so a forgotten flag cannot make a CI run pass by skipping.
    /// </summary>
    public static bool IsRequired(Func<string, string?> getVariable)
    {
        ArgumentNullException.ThrowIfNull(getVariable);

        return IsTrue(getVariable(RequireVariable))
               || IsTrue(getVariable("GITHUB_ACTIONS"))
               || IsTrue(getVariable("CI"));
    }

    /// <summary>A test is skipped only when the server is unreachable and the tests are not mandatory.</summary>
    public static bool ShouldSkip(bool required, bool serverAvailable) => !required && !serverAvailable;

    public static bool IsExperimentRequested(Func<string, string?> getVariable)
    {
        ArgumentNullException.ThrowIfNull(getVariable);

        return IsTrue(getVariable(ExperimentVariable));
    }

    public static bool IsSafeDatabaseName(string? name) => name is not null && SafeDatabaseName.IsMatch(name);

    public static string NewDatabaseName() => DatabasePrefix + Guid.NewGuid().ToString("N")[..8];

    private static bool IsTrue(string? value)
    {
        var trimmed = value?.Trim();

        return string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase) || trimmed == "1";
    }
}
