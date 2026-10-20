using ERTriage.Api.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ERTriage.Api.IntegrationTests.Http;

/// <summary>
/// One isolated database and one in-memory API per test class (class fixture). The database is created by its own
/// <see cref="SqlServerFixture"/> instance, so HTTP test classes never share data with each other or with the
/// persistence tests. Cleanup: the database is dropped in <see cref="DisposeAsync"/> (a <c>finally</c>, so it runs even
/// when disposing the host fails) and also when initialization fails after the database was created. xUnit disposes
/// class fixtures whatever the test outcome; only a killed process (for example a stopped debugger) can leave one behind.
/// </summary>
public sealed class ApiTestFixture : IAsyncLifetime
{
    /// <summary>The fixed "now" of every HTTP test (UTC).</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

    private readonly SqlServerFixture _database = new();
    private ErTriageApiFactory? _factory;

    public FixedTimeProvider Clock { get; } = new(Now);

    public CapturingLoggerProvider Logs { get; } = new();

    public string DatabaseName => _database.DatabaseName;

    public ErTriageApiFactory Factory =>
        _factory ?? throw new InvalidOperationException("SQL Server is not available; this test should have been skipped.");

    public async Task InitializeAsync()
    {
        // Throws only when SQL Server tests are mandatory (CI / ERTRIAGE_REQUIRE_SQLSERVER) and no server exists.
        await _database.InitializeAsync();

        if (!_database.IsAvailable)
        {
            return; // every test is skipped by [SqlServerFact] / [SqlServerTheory]
        }

        try
        {
            await _database.EnsureSchemaAsync();
            _factory = new ErTriageApiFactory(_database.ConnectionString, Clock, Logs);
            _factory.EnsureTargets(_database.DatabaseName);
        }
        catch
        {
            // xUnit does not dispose a fixture whose initialization failed: drop the database here.
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (_factory is not null)
            {
                await _factory.DisposeAsync();
                _factory = null;
            }
        }
        finally
        {
            await _database.DisposeAsync();
        }
    }

    /// <summary>A client for the in-memory server. English messages are requested explicitly.</summary>
    public HttpClient CreateClient()
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        return client;
    }

    /// <summary>Empties the Patients table of this fixture's database (after proving which database it is).</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var connection = await _database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT DB_NAME()";
        var name = (string?)await command.ExecuteScalarAsync();

        if (!SqlServerTestPolicy.IsSafeDatabaseName(name) || name != _database.DatabaseName)
        {
            throw new InvalidOperationException("HTTP tests must run in their own generated test database.");
        }

        command.CommandText = "DELETE FROM Patients";
        await command.ExecuteNonQueryAsync();
    }
}
