using ERTriage.Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace ERTriage.Api.IntegrationTests;

/// <summary>Checks the shipped appsettings files per environment (no host is started).</summary>
public class EnvironmentConfigurationTests
{
    private static readonly string SettingsDirectory = Path.Combine(AppContext.BaseDirectory, "ApiSettings");

    private static readonly Dictionary<string, string> ApprovedLogLevels = new()
    {
        ["Default"] = "Information",
        ["Microsoft"] = "Warning",
        ["Microsoft.Hosting.Lifetime"] = "Information",
        ["Microsoft.AspNetCore.Hosting.Diagnostics"] = "Information",
        ["Microsoft.EntityFrameworkCore"] = "Warning",
        ["Microsoft.EntityFrameworkCore.Database.Command"] = "Information",
        ["System"] = "Warning",
        ["Quartz"] = "Warning",
        ["Hangfire"] = "Warning"
    };

    private static IConfigurationRoot Load(string environment) =>
        new ConfigurationBuilder()
            .SetBasePath(SettingsDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: false)
            .Build();

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Production")]
    public void Logging_EveryEnvironment_PreservesApprovedCategories(string environment)
    {
        var levels = Load(environment).GetSection("Logging:LogLevel").GetChildren()
            .ToDictionary(c => c.Key, c => c.Value!);

        Assert.Equal(ApprovedLogLevels.OrderBy(p => p.Key), levels.OrderBy(p => p.Key));
    }

    [Fact]
    public void Development_HasAConnectionStringWithADatabaseName()
    {
        // Owner decision (2026-10-20): Development may point at the owner's hosted SQL Server instead of LocalDB.
        var connectionString = Load("Development")[ApiConfiguration.ConnectionStringKey];

        Assert.NotNull(connectionString);
        Assert.True(ApiConfiguration.TryGetDatabaseName(connectionString, out var name));
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void Test_UsesDedicatedDatabaseDifferentFromDevelopment()
    {
        var development = Load("Development")[ApiConfiguration.ConnectionStringKey]!;
        var test = Load("Test")[ApiConfiguration.ConnectionStringKey];

        Assert.NotNull(test);
        Assert.True(ApiConfiguration.TryGetDatabaseName(development, out var developmentDb));
        Assert.True(ApiConfiguration.TryGetDatabaseName(test, out var testDb));
        Assert.NotEqual(developmentDb, testDb);
        Assert.Contains("Test", testDb, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Test_DoesNotInheritDevelopmentConnectionString()
    {
        // Only the Test file is layered over the base file; Development values must never leak in.
        var baseOnly = new ConfigurationBuilder()
            .SetBasePath(SettingsDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        Assert.Null(baseOnly[ApiConfiguration.ConnectionStringKey]);
    }

    [Fact]
    public void Production_ShipsNoConnectionStringAndNoOrigins()
    {
        var configuration = Load("Production");

        Assert.Null(configuration[ApiConfiguration.ConnectionStringKey]);
        Assert.Empty(ApiConfiguration.GetAllowedOrigins(configuration));
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Test", true)]
    [InlineData("Production", false)]
    public void ApiDocumentation_PerEnvironment_IsEnabledOnlyOutsideProduction(string environment, bool expected)
    {
        Assert.Equal(expected, ApiConfiguration.IsApiDocumentationEnabled(Load(environment)));
    }

    [Fact]
    public void ApiDocumentation_BaseConfiguration_IsDisabledByDefault()
    {
        var baseOnly = new ConfigurationBuilder()
            .SetBasePath(SettingsDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        Assert.False(ApiConfiguration.IsApiDocumentationEnabled(baseOnly));
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Test", false)]
    [InlineData("Production", false)]
    public void MigrateOnStartup_PerEnvironment_IsEnabledOnlyInDevelopment(string environment, bool expected)
    {
        Assert.Equal(expected, ApiConfiguration.IsMigrateOnStartupEnabled(Load(environment)));
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    [InlineData("appsettings.Test.json")]
    [InlineData("appsettings.Production.json")]
    public void SettingsFiles_Shipped_ContainNoCredentials(string fileName)
    {
        var content = File.ReadAllText(Path.Combine(SettingsDirectory, fileName));

        // Owner decision (2026-10-20): the Development file may carry the hosted database credentials.
        // Every other shipped file (base, Test, Production) must stay credential-free.
        if (!fileName.Equals("appsettings.Development.json", StringComparison.OrdinalIgnoreCase))
        {
            Assert.DoesNotContain("password=", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pwd=", content, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("apikey", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", content, StringComparison.OrdinalIgnoreCase);
    }
}
