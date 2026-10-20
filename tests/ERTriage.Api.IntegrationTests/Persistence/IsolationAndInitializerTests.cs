using ERTriage.Application.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace ERTriage.Api.IntegrationTests.Persistence;

/// <summary>
/// Cleanup of the isolated database in normal and failure scenarios, and the real startup initializer (S7a) on a
/// brand-new empty database. Each test owns a separate generated database, never the shared one.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class IsolationAndInitializerTests
{
    private static async Task<bool> DatabaseExistsAsync(string name)
    {
        var admin = SqlServerTestServer.Current.AdminConnectionString
            ?? throw new InvalidOperationException("SQL Server is not available.");

        await using var connection = new SqlConnection(admin);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name";
        command.Parameters.AddWithValue("@name", name);

        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    [SqlServerFact]
    public async Task Fixture_CreatesAnIsolatedDatabaseAndDropsItOnDispose()
    {
        var isolated = new SqlServerFixture();
        await isolated.InitializeAsync();
        var name = isolated.DatabaseName;

        try
        {
            Assert.True(SqlServerTestPolicy.IsSafeDatabaseName(name));
            Assert.True(await DatabaseExistsAsync(name));
        }
        finally
        {
            // Always drop, even if an assertion above fails, so a failing run never leaves a database behind.
            await isolated.DisposeAsync();
        }

        Assert.False(await DatabaseExistsAsync(name));
        await isolated.DisposeAsync(); // a second dispose is harmless
    }

    [SqlServerFact]
    public async Task Fixture_DropsTheDatabaseEvenWhenAConnectionIsStillOpen()
    {
        var isolated = new SqlServerFixture();
        await isolated.InitializeAsync();
        var name = isolated.DatabaseName;
        var leaked = await isolated.OpenConnectionAsync();

        try
        {
            await isolated.DisposeAsync();
        }
        finally
        {
            await leaked.DisposeAsync();
        }

        Assert.False(await DatabaseExistsAsync(name));
    }

    [SqlServerFact]
    public async Task Fixture_DropsTheDatabaseWhenTheTestBodyFails()
    {
        var isolated = new SqlServerFixture();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            try
            {
                await isolated.InitializeAsync();
                await isolated.EnsureSchemaAsync();
                throw new InvalidOperationException("simulated test failure");
            }
            finally
            {
                await isolated.DisposeAsync();
            }
        });

        Assert.False(await DatabaseExistsAsync(isolated.DatabaseName));
    }

    [SqlServerFact]
    public async Task DatabaseInitializer_MigratesAnEmptyDatabase_AndIsIdempotent()
    {
        var isolated = new SqlServerFixture();
        await isolated.InitializeAsync();

        try
        {
            var logs = new CapturingLoggerProvider();
            await using var provider = PersistenceTestHost.Build(isolated.ConnectionString, logs);

            await RunInitializerAsync(provider);

            await using (var connection = await isolated.OpenConnectionAsync())
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT OBJECT_ID(N'Patients')";
                Assert.IsType<int>(await command.ExecuteScalarAsync());
            }

            Assert.Contains(
                logs.Entries,
                e => e.Category.EndsWith("DatabaseInitializer", StringComparison.Ordinal)
                     && e.Message.Contains("Applying", StringComparison.Ordinal)
                     && e.Message.Contains("InitialCreate", StringComparison.Ordinal));

            logs.Clear();
            await RunInitializerAsync(provider);

            Assert.Contains(
                logs.Entries,
                e => e.Category.EndsWith("DatabaseInitializer", StringComparison.Ordinal)
                     && e.Message.Contains("up to date", StringComparison.Ordinal));
            Assert.DoesNotContain(
                logs.Entries,
                e => e.Message.Contains("Applying", StringComparison.Ordinal));
        }
        finally
        {
            await isolated.DisposeAsync();
        }
    }

    private static async Task RunInitializerAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>().MigrateAsync(CancellationToken.None);
    }
}
