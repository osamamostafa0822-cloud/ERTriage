using ERTriage.Api.Configuration;
using ERTriage.Application;
using ERTriage.Application.Common;
using ERTriage.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ERTriage.Api.IntegrationTests;

/// <summary>Startup migration gating (D-44, D-45). No database is opened.</summary>
public class MigrateOnStartupTests
{
    private const string LocalDb = "Server=(localdb)\\MSSQLLocalDB;Database={0};Trusted_Connection=True;TrustServerCertificate=True";
    private const string RemoteServer = "Server=db.example.org;Database=ERTriage;User Id=app;Encrypt=True";

    private sealed class FakeHostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "ERTriage.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(string environment, string? migrate)
    {
        var values = new Dictionary<string, string?>
        {
            [ApiConfiguration.ConnectionStringKey] = environment switch
            {
                "Production" => RemoteServer,
                "Test" => string.Format(LocalDb, "ERTriage_Test"),
                _ => string.Format(LocalDb, "ERTriage")
            },
            [ApiConfiguration.AllowedOriginsKey + ":0"] = "https://triage.example.org"
        };

        if (migrate is not null)
        {
            values[ApiConfiguration.MigrateOnStartupKey] = migrate;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static void Validate(string environment, string? migrate) =>
        ApiConfiguration.Validate(Config(environment, migrate), new FakeHostEnvironment(environment));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("not-a-bool", false)]
    public void IsMigrateOnStartupEnabled_OnlyWhenExplicitlyTrue(string? value, bool expected) =>
        Assert.Equal(expected, ApiConfiguration.IsMigrateOnStartupEnabled(Config("Development", value)));

    [Theory]
    [InlineData("Development", "true")]
    [InlineData("Development", "false")]
    [InlineData("Test", "false")]
    [InlineData("Production", "false")]
    [InlineData("Production", null)]
    public void Validate_AllowedCombination_DoesNotThrow(string environment, string? migrate) =>
        Validate(environment, migrate);

    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    public void Validate_MigrateOnStartupInProduction_IsRejectedWithoutEchoingSettings(string environment)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Validate(environment, "true"));

        Assert.Contains(ApiConfiguration.MigrateOnStartupKey, ex.Message);
        Assert.DoesNotContain("db.example.org", ex.Message);
    }

    [Fact]
    public void Validate_UnparseableValue_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Validate("Development", "maybe"));

        Assert.Contains(ApiConfiguration.MigrateOnStartupKey, ex.Message);
    }

    [Fact]
    public void AddInfrastructure_Registered_ResolvesDatabaseInitializerPerScope()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ApiConfiguration.ConnectionStringKey] = string.Format(LocalDb, "ERTriage_DiTest")
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddLogging()
            .AddApplication()
            .AddInfrastructure(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var a1 = scopeA.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
        var a2 = scopeA.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
        var b = scopeB.ServiceProvider.GetRequiredService<IDatabaseInitializer>();

        Assert.Same(a1, a2);
        Assert.NotSame(a1, b);
    }
}
