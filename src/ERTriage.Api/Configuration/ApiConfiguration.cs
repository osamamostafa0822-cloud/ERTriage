using System.Data.Common;

namespace ERTriage.Api.Configuration;

/// <summary>
/// Reads Api-level settings and validates the configuration at startup (fail fast, environment-config rules).
/// Messages never echo connection strings or other setting values.
/// </summary>
public static class ApiConfiguration
{
    public const string TestEnvironment = "Test";
    public const string ConnectionStringKey = "ConnectionStrings:ErTriage";
    public const string AllowedOriginsKey = "Cors:AllowedOrigins";
    public const string ApiDocumentationEnabledKey = "ApiDocumentation:Enabled";

    private static readonly string[] SupportedEnvironments =
        [Environments.Development, TestEnvironment, Environments.Production];

    public static string[] GetAllowedOrigins(IConfiguration configuration) =>
        configuration.GetSection(AllowedOriginsKey).Get<string[]>() ?? [];

    public static bool IsApiDocumentationEnabled(IConfiguration configuration) =>
        configuration.GetValue<bool>(ApiDocumentationEnabledKey);

    /// <summary>Extracts the database name ("Database" or "Initial Catalog") without any SQL Server dependency.</summary>
    public static bool TryGetDatabaseName(string connectionString, out string? databaseName)
    {
        databaseName = null;
        var builder = new DbConnectionStringBuilder();

        try
        {
            builder.ConnectionString = connectionString;
        }
        catch (ArgumentException)
        {
            // Not a parseable connection string: reported by the caller as a configuration error.
            return false;
        }

        foreach (var key in new[] { "Database", "Initial Catalog" })
        {
            if (builder.TryGetValue(key, out var value) && value is string name && name.Length > 0)
            {
                databaseName = name;
                return true;
            }
        }

        return true;
    }

    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        List<string> errors = [];
        var environmentName = environment.EnvironmentName;
        var isTest = IsEnvironment(environmentName, TestEnvironment);
        var isProduction = IsEnvironment(environmentName, Environments.Production);

        if (!SupportedEnvironments.Contains(environmentName, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Environment '{environmentName}' is not supported. Use Development, Test or Production.");
        }

        ValidateAllowedOrigins(configuration, isProduction, errors);
        ValidateConnectionString(configuration, isTest, isProduction, errors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid configuration for environment '{environmentName}': {string.Join(" ", errors)}");
        }
    }

    private static void ValidateAllowedOrigins(IConfiguration configuration, bool isProduction, List<string> errors)
    {
        var origins = GetAllowedOrigins(configuration);

        if (origins.Any(origin => origin.Trim() == "*"))
        {
            errors.Add($"'{AllowedOriginsKey}' must list explicit origins; '*' is not allowed.");
        }

        if (isProduction && origins.Length == 0)
        {
            errors.Add($"'{AllowedOriginsKey}' must be provided in Production (for example via environment variables).");
        }
    }

    private static void ValidateConnectionString(
        IConfiguration configuration,
        bool isTest,
        bool isProduction,
        List<string> errors)
    {
        // A missing connection string is reported by AddInfrastructure (the owner of persistence configuration).
        var connectionString = configuration[ConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        if (!TryGetDatabaseName(connectionString, out var databaseName))
        {
            errors.Add($"'{ConnectionStringKey}' is not a valid connection string.");
            return;
        }

        if (isTest && (databaseName is null || !databaseName.Contains("Test", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add(
                $"'{ConnectionStringKey}' must target a dedicated test database (name containing 'Test') in the Test environment.");
        }

        if (isProduction && UsesLocalDb(connectionString))
        {
            errors.Add($"'{ConnectionStringKey}' must not point to LocalDB in Production.");
        }
    }

    private static bool UsesLocalDb(string connectionString)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

        foreach (var key in new[] { "Server", "Data Source" })
        {
            if (builder.TryGetValue(key, out var value)
                && value is string server
                && server.Contains("(localdb)", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEnvironment(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}
