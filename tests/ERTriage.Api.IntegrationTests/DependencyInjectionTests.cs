using ERTriage.Application;
using ERTriage.Application.Common;
using ERTriage.Infrastructure;
using ERTriage.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ERTriage.Application.Patients.Services;
using ERTriage.Application.Triage.Services;
using ERTriage.Domain.Patients;
using ERTriage.Domain.Common;
using ERTriage.Domain.Triage;

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
    public void AddInfrastructure_Registered_ResolvesTriageBoardReadRepositoryPerScope()
    {
        using var provider = BuildProvider();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var readerA1 = scopeA.ServiceProvider.GetRequiredService<ITriageBoardReadRepository>();
        var readerA2 = scopeA.ServiceProvider.GetRequiredService<ITriageBoardReadRepository>();
        var readerB = scopeB.ServiceProvider.GetRequiredService<ITriageBoardReadRepository>();

        Assert.Same(readerA1, readerA2);
        Assert.NotSame(readerA1, readerB);
    }

    [Fact]
    public void AddApplication_Registered_ResolvesUseCaseServicesPerScope()
    {
        using var provider = BuildProvider();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var registrationA1 = scopeA.ServiceProvider.GetRequiredService<IPatientRegistrationService>();
        var registrationA2 = scopeA.ServiceProvider.GetRequiredService<IPatientRegistrationService>();
        var registrationB = scopeB.ServiceProvider.GetRequiredService<IPatientRegistrationService>();

        Assert.Same(registrationA1, registrationA2);
        Assert.NotSame(registrationA1, registrationB);
        Assert.NotNull(scopeA.ServiceProvider.GetRequiredService<IPatientWorkflowService>());
        Assert.NotNull(scopeA.ServiceProvider.GetRequiredService<ITriageBoardService>());
    }

    [Fact]
    public void AddApplication_WithoutAnyHostRegistration_StillProvidesTheSystemClock()
    {
        using var provider = BuildProvider();

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void AddApplication_ClockRegisteredBeforehand_IsNotReplaced()
    {
        var fake = new FixedClock();

        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(fake)
            .AddApplication()
            .AddInfrastructure(ConfigurationWithConnectionString())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Assert.Same(fake, provider.GetRequiredService<TimeProvider>());
    }

    private sealed class FixedClock : TimeProvider
    {
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
