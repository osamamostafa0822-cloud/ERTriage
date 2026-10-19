using ERTriage.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ERTriage.Api.IntegrationTests;

public class ApiConfigurationValidatorTests
{
    private const string LocalDb = "Server=(localdb)\\MSSQLLocalDB;Database={0};Trusted_Connection=True;TrustServerCertificate=True";
    private const string RemoteServer = "Server=sql.example.internal;Database=ERTriage;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class FakeHostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "ERTriage.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(string? connectionString, params string[] origins)
    {
        var values = new Dictionary<string, string?>();
        if (connectionString is not null)
        {
            values[ApiConfiguration.ConnectionStringKey] = connectionString;
        }

        for (var i = 0; i < origins.Length; i++)
        {
            values[$"{ApiConfiguration.AllowedOriginsKey}:{i}"] = origins[i];
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static void Validate(string environment, IConfiguration configuration) =>
        ApiConfiguration.Validate(configuration, new FakeHostEnvironment(environment));

    [Fact]
    public void Validate_DevelopmentWithLocalDb_Passes() =>
        Validate("Development", Config(string.Format(LocalDb, "ERTriage"), "http://localhost:4200"));

    [Theory]
    [InlineData("ERTriage_Test")]
    [InlineData("ERTriage_Tests_3f2a9c")]
    public void Validate_TestWithDedicatedDatabase_Passes(string database) =>
        Validate("Test", Config(string.Format(LocalDb, database)));

    [Fact]
    public void Validate_TestPointingAtDevelopmentDatabase_Fails()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Validate("Test", Config(string.Format(LocalDb, "ERTriage"))));

        Assert.Contains("dedicated test database", exception.Message);
        Assert.DoesNotContain("LocalDB", exception.Message); // never echoes the connection string
        Assert.DoesNotContain("Trusted_Connection", exception.Message);
    }

    [Fact]
    public void Validate_ProductionWithRemoteServerAndOrigin_Passes() =>
        Validate("Production", Config(RemoteServer, "https://triage.example.org"));

    [Fact]
    public void Validate_ProductionWithLocalDb_Fails()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Validate("Production", Config(string.Format(LocalDb, "ERTriage"), "https://triage.example.org")));

        Assert.Contains("LocalDB", exception.Message);
    }

    [Fact]
    public void Validate_ProductionWithoutOrigins_Fails()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Validate("Production", Config(RemoteServer)));

        Assert.Contains(ApiConfiguration.AllowedOriginsKey, exception.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Validate_WildcardOrigin_Fails(string environment)
    {
        Assert.Throws<InvalidOperationException>(
            () => Validate(environment, Config(RemoteServer, "*")));
    }

    [Fact]
    public void Validate_UnsupportedEnvironment_Fails()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Validate("Staging", Config(RemoteServer, "https://triage.example.org")));

        Assert.Contains("not supported", exception.Message);
    }

    [Fact]
    public void Validate_UnparseableConnectionString_Fails()
    {
        Assert.Throws<InvalidOperationException>(
            () => Validate("Development", Config("not-a-connection-string")));
    }

    [Fact]
    public void Validate_MissingConnectionString_IsLeftToInfrastructure() =>
        Validate("Development", Config(null, "http://localhost:4200"));
}
