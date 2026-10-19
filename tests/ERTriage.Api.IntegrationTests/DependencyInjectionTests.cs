using ERTriage.Application;
using ERTriage.Application.Common;
using ERTriage.Application.Patients;
using ERTriage.Infrastructure;
using ERTriage.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERTriage.Api.IntegrationTests;

/// <summary>Verifies container wiring without opening a database connection.</summary>
public class DependencyInjectionTests
{
    private static IConfiguration ConfigurationWithConnectionString() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ErTriage"] = "Server=(localdb)\\MSSQLLocalDB;Database=ERTriage_DiTest;Trusted_Connection=True;TrustServerCertificate=True"
            })
            .Build();

    private static ServiceProvider BuildProvider() =>
        new ServiceCollection()
            .AddLogging()
            .AddApplication()
            .AddInfrastructure(ConfigurationWithConnectionString())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    [Fact]
    public void AddInfrastructure_Registered_ResolvesUnitOfWorkAndDbContextPerScope()
    {
        using var provider = BuildProvider();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var uowA1 = scopeA.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var uowA2 = scopeA.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var uowB = scopeB.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Assert.Same(uowA1, uowA2);
        Assert.NotSame(uowA1, uowB);
        Assert.NotSame(
            scopeA.ServiceProvider.GetRequiredService<ErTriageDbContext>(),
            scopeB.ServiceProvider.GetRequiredService<ErTriageDbContext>());
    }

    [Fact]
    public void AddInfrastructure_Registered_ResolvesPatientRepositoryPerScope()
    {
        using var provider = BuildProvider();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var repositoryA1 = scopeA.ServiceProvider.GetRequiredService<IPatientRepository>();
        var repositoryA2 = scopeA.ServiceProvider.GetRequiredService<IPatientRepository>();
        var repositoryB = scopeB.ServiceProvider.GetRequiredService<IPatientRepository>();

        Assert.Same(repositoryA1, repositoryA2);
        Assert.NotSame(repositoryA1, repositoryB);
    }

    [Fact]
    public void AddInfrastructure_MissingConnectionString_FailsFast()
    {
        var emptyConfiguration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddInfrastructure(emptyConfiguration));

        Assert.Contains("ErTriage", exception.Message);
    }
}
