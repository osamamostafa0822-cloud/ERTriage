using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace ERTriage.Api.IntegrationTests.Persistence;

/// <summary>Result of looking for a SQL Server to run the integration tests on. Never contains connection details in its reason.</summary>
internal sealed record ServerInfo(string? AdminConnectionString, string? UnavailableReason)
{
    public bool IsAvailable => UnavailableReason is null;

    public static ServerInfo Unavailable(string reason) => new(null, reason);
}

/// <summary>
/// Finds the SQL Server for the tests, once per process. Source: <c>ERTRIAGE_TEST_SQLSERVER</c> (a server-level
/// connection string, used by CI) or, when unset, the LocalDB server named in the shipped <c>appsettings.Test.json</c>.
/// The Development settings file is never read. Only the <c>master</c> database is opened here (to prove the server
/// answers); the fixture later opens <c>master</c> to create/drop and the generated test database to work in.
/// </summary>
internal static class SqlServerTestServer
{
    private static readonly Lazy<ServerInfo> Discovered = new(Discover);

    public static ServerInfo Current => Discovered.Value;

    /// <summary>The skip message for a test, or <c>null</c> when it must run (or fail, when the tests are mandatory).</summary>
    public static string? SkipReason(Func<string, string?> getVariable)
    {
        var server = Current;

        return SqlServerTestPolicy.ShouldSkip(SqlServerTestPolicy.IsRequired(getVariable), server.IsAvailable)
            ? $"SQL Server is not available ({server.UnavailableReason}). Set {SqlServerTestPolicy.RequireVariable}=true to fail instead of skipping."
            : null;
    }

    private static ServerInfo Discover()
    {
        var connectionString = Environment.GetEnvironmentVariable(SqlServerTestPolicy.ServerVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = ReadTestSettingsConnectionString();
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return ServerInfo.Unavailable("no connection string configured");
        }

        SqlConnectionStringBuilder builder;

        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            return ServerInfo.Unavailable("the connection string is not valid");
        }

        builder.InitialCatalog = "master";

        try
        {
            // Synchronous on purpose: this runs once during test discovery, from attribute constructors that cannot await.
            using var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.ExecuteScalar();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            return ServerInfo.Unavailable(Describe(ex));
        }

        return new ServerInfo(builder.ConnectionString, null);
    }

    private static string? ReadTestSettingsConnectionString()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ApiSettings");

        return new ConfigurationBuilder()
            .SetBasePath(directory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Test.json", optional: true)
            .Build()["ConnectionStrings:ErTriage"];
    }

    private static string Describe(Exception ex) =>
        ex is SqlException sql ? $"{nameof(SqlException)} error {sql.Number}" : ex.GetType().Name;
}
