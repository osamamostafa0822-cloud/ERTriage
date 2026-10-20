using ERTriage.Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace ERTriage.Api.IntegrationTests.Persistence;

/// <summary>Tests of the pure SQL Server test rules. No database is needed, so they always run (also in CI).</summary>
public sealed class SqlServerTestPolicyTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] values) =>
        name => values.Where(v => v.Name == name).Select(v => v.Value).FirstOrDefault();

    [Fact]
    public void IsRequired_NoVariables_IsFalse() =>
        Assert.False(SqlServerTestPolicy.IsRequired(Env()));

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("1")]
    public void IsRequired_ExplicitFlagOn_IsTrue(string value) =>
        Assert.True(SqlServerTestPolicy.IsRequired(Env((SqlServerTestPolicy.RequireVariable, value))));

    [Theory]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("")]
    public void IsRequired_ExplicitFlagOff_IsFalse(string value) =>
        Assert.False(SqlServerTestPolicy.IsRequired(Env((SqlServerTestPolicy.RequireVariable, value))));

    [Theory]
    [InlineData("GITHUB_ACTIONS", "true")]
    [InlineData("CI", "true")]
    [InlineData("CI", "1")]
    public void IsRequired_RunningInCi_IsTrueEvenWithoutTheFlag(string variable, string value) =>
        Assert.True(SqlServerTestPolicy.IsRequired(Env((variable, value))));

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void ShouldSkip_OnlyWhenServerMissingAndNotRequired(bool required, bool available, bool expected) =>
        Assert.Equal(expected, SqlServerTestPolicy.ShouldSkip(required, available));

    [Theory]
    [InlineData("ERTriage_Test_0a1b2c3d", true)]
    [InlineData("ERTriage", false)]
    [InlineData("ERTriage_Test", false)]
    [InlineData("master", false)]
    [InlineData("", false)]
    [InlineData("ERTriage_Test_0A1B2C3D", false)]
    [InlineData("ERTriage_Test_0a1b2c3", false)]
    [InlineData("ERTriage_Test_0a1b2c3d9", false)]
    [InlineData("ERTriage_Test_0a1b2c3d\n", false)]
    [InlineData("ERTriage_Test_0a1b2c3d]; DROP DATABASE x;--", false)]
    public void IsSafeDatabaseName_OnlyGeneratedTestNames_AreAccepted(string name, bool expected) =>
        Assert.Equal(expected, SqlServerTestPolicy.IsSafeDatabaseName(name));

    [Fact]
    public void IsSafeDatabaseName_Null_IsRejected() =>
        Assert.False(SqlServerTestPolicy.IsSafeDatabaseName(null));

    [Fact]
    public void NewDatabaseName_Generated_IsSafeAndUnique()
    {
        var names = Enumerable.Range(0, 100).Select(_ => SqlServerTestPolicy.NewDatabaseName()).ToList();

        Assert.All(names, name => Assert.True(SqlServerTestPolicy.IsSafeDatabaseName(name)));
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void ShippedDatabaseNames_AreNeverAcceptedAsDroppable(string environment)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ApiSettings");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(directory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: false)
            .Build();

        var connectionString = configuration[ApiConfiguration.ConnectionStringKey];

        Assert.NotNull(connectionString);
        Assert.True(ApiConfiguration.TryGetDatabaseName(connectionString, out var name));
        Assert.False(SqlServerTestPolicy.IsSafeDatabaseName(name));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData(null, false)]
    public void IsExperimentRequested_OnlyWhenExplicitlyEnabled(string? value, bool expected)
    {
        var env = value is null ? Env() : Env((SqlServerTestPolicy.ExperimentVariable, value));

        Assert.Equal(expected, SqlServerTestPolicy.IsExperimentRequested(env));
    }
}
