using ERTriage.Api.Configuration;
using ERTriage.Application;
using ERTriage.Application.Common;
using ERTriage.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ERTriage.Api.IntegrationTests.Seed;

/// <summary>
/// The seed can never run in Test or Production (D-45, D-58): configuration validation, shipped settings and a real
/// startup of <c>Program</c>. No database is opened: startup fails during configuration validation, before any query.
/// </summary>
public class SeedEnvironmentSafetyTests
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

    private static IConfiguration Config(string environment, string? seed) =>
        new ConfigurationBuilder().AddInMemoryCollection(Values(environment, seed)).Build();

    private static Dictionary<string, string?> Values(string environment, string? seed)
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

        if (seed is not null)
        {
            values[ApiConfiguration.SeedDevelopmentDataKey] = seed;
        }

        return values;
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("yes", false)]
    public void IsSeedDevelopmentDataEnabled_OnlyWhenExplicitlyTrue(string? value, bool expected) =>
        Assert.Equal(expected, ApiConfiguration.IsSeedDevelopmentDataEnabled(Config("Development", value)));

    [Theory]
    [InlineData("Development", "true")]
    [InlineData("Development", "false")]
    [InlineData("Development", null)]
    [InlineData("Test", "false")]
    [InlineData("Test", null)]
    [InlineData("Production", "false")]
    [InlineData("Production", null)]
    public void Validate_AllowedCombination_DoesNotThrow(string environment, string? seed) =>
        ApiConfiguration.Validate(Config(environment, seed), new FakeHostEnvironment(environment));

    [Theory]
    [InlineData("Test")]
    [InlineData("test")]
    [InlineData("Production")]
    [InlineData("PRODUCTION")]
    public void Validate_SeedEnabledOutsideDevelopment_IsRejectedWithoutEchoingSettings(string environment)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ApiConfiguration.Validate(Config(environment, "true"), new FakeHostEnvironment(environment)));

        Assert.Contains(ApiConfiguration.SeedDevelopmentDataKey, ex.Message);
        Assert.Contains("only be enabled in the Development environment", ex.Message);
        Assert.DoesNotContain("db.example.org", ex.Message);
    }

    [Fact]
    public void Validate_UnparseableSeedValue_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ApiConfiguration.Validate(Config("Development", "maybe"), new FakeHostEnvironment("Development")));

        Assert.Contains(ApiConfiguration.SeedDevelopmentDataKey, ex.Message);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Test", false)]
    [InlineData("Production", false)]
    public void ShippedSettings_EnableSeedingOnlyInDevelopment(string environment, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(AppContext.BaseDirectory, "ApiSettings"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: false)
            .Build();

        Assert.Equal(expected, ApiConfiguration.IsSeedDevelopmentDataEnabled(configuration));
    }

    [Theory]
    [InlineData("Test")]
    [InlineData("Production")]
    public void Startup_WithSeedForcedOnOutsideDevelopment_FailsBeforeTouchingAnyDatabase(string environment)
    {
        using var factory = new SeedFlagFactory(environment, Values(environment, "true"));

        var ex = Record.Exception(() => factory.Services);

        Assert.NotNull(ex);
        Assert.Contains(ApiConfiguration.SeedDevelopmentDataKey, FullMessage(ex));
    }

    [Fact]
    public void AddInfrastructure_Registered_ResolvesSeederPerScope()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddApplication()
            .AddInfrastructure(Config("Development", null))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var a1 = scopeA.ServiceProvider.GetRequiredService<IDevelopmentDataSeeder>();
        var a2 = scopeA.ServiceProvider.GetRequiredService<IDevelopmentDataSeeder>();
        var b = scopeB.ServiceProvider.GetRequiredService<IDevelopmentDataSeeder>();

        Assert.Same(a1, a2);
        Assert.NotSame(a1, b);
    }

    private static string FullMessage(Exception ex)
    {
        var messages = new List<string>();
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" -> ", messages);
    }

    /// <summary>Starts the real Program with the given environment and settings; nothing else is replaced.</summary>
    private sealed class SeedFlagFactory(string environment, IReadOnlyDictionary<string, string?> settings)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }
}
