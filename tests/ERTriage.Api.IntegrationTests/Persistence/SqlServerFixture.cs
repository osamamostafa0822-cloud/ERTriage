using Microsoft.Data.SqlClient;

namespace ERTriage.Api.IntegrationTests.Persistence;

/// <summary>
/// One isolated, empty SQL Server database per test run, named <c>ERTriage_Test_{8 hex}</c> (D-47). The fixture only
/// ever opens two databases: <c>master</c> (to create and drop) and the database it generated. It refuses any name that
/// is not a generated test name, so the Development database, the API's <c>ERTriage_Test</c> database and any
/// Production database can never be created, modified or dropped by it. The schema is not created here (migrations
/// arrive in S5/S6).
///
/// When no server is reachable: the tests are skipped by <see cref="SqlServerFactAttribute"/> unless they are
/// mandatory (CI or <c>ERTRIAGE_REQUIRE_SQLSERVER=true</c>), in which case initialization throws and every test in the
/// collection fails.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private string? _adminConnectionString;

    public bool IsAvailable { get; private set; }

    public string DatabaseName { get; private set; } = string.Empty;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var server = SqlServerTestServer.Current;

        if (!server.IsAvailable)
        {
            if (SqlServerTestPolicy.IsRequired(Environment.GetEnvironmentVariable))
            {
                throw new InvalidOperationException(
                    $"SQL Server integration tests are mandatory (CI or {SqlServerTestPolicy.RequireVariable}) but no server is available: {server.UnavailableReason}.");
            }

            return;
        }

        var admin = server.AdminConnectionString
            ?? throw new InvalidOperationException("The SQL Server connection string is missing.");

        var databaseName = SqlServerTestPolicy.NewDatabaseName();
        EnsureSafe(databaseName);

        _adminConnectionString = admin;
        await ExecuteAdminAsync($"CREATE DATABASE [{databaseName}]");

        DatabaseName = databaseName;
        ConnectionString = new SqlConnectionStringBuilder(admin) { InitialCatalog = databaseName }.ConnectionString;
        IsAvailable = true;
    }

    public async Task DisposeAsync()
    {
        if (!IsAvailable)
        {
            return;
        }

        EnsureSafe(DatabaseName);

        // Pooled connections would keep the database busy.
        SqlConnection.ClearAllPools();

        await ExecuteAdminAsync(
            $"IF DB_ID(N'{DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]; END");

        IsAvailable = false;
    }

    /// <summary>Opens a connection to the isolated database. The caller disposes it.</summary>
    public async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("SQL Server is not available; this test should have been skipped.");
        }

        var connection = new SqlConnection(ConnectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static void EnsureSafe(string databaseName)
    {
        // DDL cannot take the database name as a parameter, so it is whitelisted before it is placed in the statement.
        if (!SqlServerTestPolicy.IsSafeDatabaseName(databaseName))
        {
            throw new InvalidOperationException("Refusing to touch a database whose name is not a generated test database name.");
        }
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new SqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
