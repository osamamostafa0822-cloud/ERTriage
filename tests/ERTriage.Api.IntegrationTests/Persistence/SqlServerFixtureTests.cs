namespace ERTriage.Api.IntegrationTests.Persistence;

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerFixtureTests
{
    private readonly SqlServerFixture _fixture;

    public SqlServerFixtureTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [SqlServerFact]
    public async Task Fixture_Initialized_WorksInAGeneratedIsolatedDatabase()
    {
        await using var connection = await _fixture.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_NAME()";

        var current = (string?)await command.ExecuteScalarAsync();

        Assert.Equal(_fixture.DatabaseName, current);
        Assert.True(SqlServerTestPolicy.IsSafeDatabaseName(current));
        Assert.NotEqual("ERTriage", current);
        Assert.NotEqual("ERTriage_Test", current);
    }
}
