using System.Data;
using ERTriage.Application.Common;
using ERTriage.Application.Patients;
using ERTriage.Domain.Patients;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace ERTriage.Api.IntegrationTests.Persistence;

/// <summary>
/// Raw access to the isolated, migrated database. Before every test it proves it is connected to a generated test
/// database (never <c>ERTriage</c>) and empties the <c>Patients</c> table, so tests do not depend on each other.
/// </summary>
public abstract class DatabaseTestBase(SqlServerFixture fixture) : IAsyncLifetime
{
    protected SqlServerFixture Fixture { get; } = fixture;

    public virtual async Task InitializeAsync()
    {
        await Fixture.EnsureSchemaAsync();

        var database = (string?)await ScalarAsync("SELECT DB_NAME()");

        if (!SqlServerTestPolicy.IsSafeDatabaseName(database))
        {
            throw new InvalidOperationException("Persistence tests must run in a generated test database.");
        }

        await ExecuteAsync("DELETE FROM Patients");
    }

    public virtual Task DisposeAsync() => Task.CompletedTask;

    protected async Task<int> ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = await Fixture.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);

        return await command.ExecuteNonQueryAsync();
    }

    protected async Task<object?> ScalarAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = await Fixture.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);

        return await command.ExecuteScalarAsync();
    }

    protected async Task<List<T>> ReadAsync<T>(string sql, Func<SqlDataReader, T> map, params SqlParameter[] parameters)
    {
        await using var connection = await Fixture.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();

        while (await reader.ReadAsync())
        {
            rows.Add(map(reader));
        }

        return rows;
    }
}

/// <summary>Adds the real Application/Infrastructure container (repositories, Unit of Work) on the isolated database.</summary>
public abstract class PersistenceTestBase(SqlServerFixture fixture) : DatabaseTestBase(fixture)
{
    public static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

    private ServiceProvider? _provider;

    protected FixedTimeProvider Clock { get; } = new(Now);

    protected CapturingLoggerProvider Logs { get; } = new();

    protected IServiceProvider Services => _provider ?? throw new InvalidOperationException("Not initialized.");

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _provider = PersistenceTestHost.Build(Fixture.ConnectionString, Logs);
    }

    public override async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }

    /// <summary>Saves patients through the real repository and Unit of Work, in their own scope.</summary>
    protected async Task SeedAsync(params Patient[] patients)
    {
        await using var scope = Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPatientRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        foreach (var patient in patients)
        {
            repository.Add(patient);
        }

        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    protected static SqlParameter IdParameter(Guid id) => new("@id", SqlDbType.UniqueIdentifier) { Value = id };
}
