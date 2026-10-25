using System.Reflection;
using ERTriage.Api;
using ERTriage.Domain.Common;

namespace ERTriage.Api.IntegrationTests;

/// <summary>Enforces dependency-rules.md using the referenced-assembly lists of each layer.</summary>
public class ArchitectureTests
{
    private const string EfCore = "Microsoft.EntityFrameworkCore";
    private const string DomainName = "ERTriage.Domain";
    private const string ApplicationName = "ERTriage.Application";
    private const string InfrastructureName = "ERTriage.Infrastructure";
    private const string ApiName = "ERTriage.Api";

    private static readonly Assembly DomainAssembly = typeof(DomainException).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(ERTriage.Application.DependencyInjection).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(ERTriage.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly ApiAssembly = typeof(ApiDependencyInjection).Assembly;

    [Fact]
    public void Assemblies_AreNamedAfterLayers()
    {
        Assert.Equal(DomainName, DomainAssembly.GetName().Name);
        Assert.Equal(ApplicationName, ApplicationAssembly.GetName().Name);
        Assert.Equal(InfrastructureName, InfrastructureAssembly.GetName().Name);
        Assert.Equal(ApiName, ApiAssembly.GetName().Name);
    }

    [Fact]
    public void Domain_ReferencesNoOtherLayerAndNoFrameworks()
    {
        var references = ReferencedNames(DomainAssembly);

        Assert.DoesNotContain(references, n => n.StartsWith("ERTriage.", StringComparison.Ordinal));
        Assert.DoesNotContain(references, n => n.StartsWith(EfCore, StringComparison.Ordinal));
        Assert.DoesNotContain(references, n => n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_ReferencesOnlyDomain_AndNoEfCore()
    {
        var references = ReferencedNames(ApplicationAssembly);

        Assert.DoesNotContain(references, n => n is InfrastructureName or ApiName);
        Assert.DoesNotContain(references, n => n.StartsWith(EfCore, StringComparison.Ordinal));
        Assert.DoesNotContain(references, n => n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void PersistencePorts_AreOwnedByTheDomain()
    {
        Assert.Same(DomainAssembly, typeof(ERTriage.Domain.Patients.IPatientRepository).Assembly);
        Assert.Same(DomainAssembly, typeof(ERTriage.Domain.Common.IUnitOfWork).Assembly);
        Assert.Same(DomainAssembly, typeof(ERTriage.Domain.Triage.ITriageBoardReadRepository).Assembly);
    }

    [Fact]
    public void Infrastructure_DoesNotReferenceApi()
    {
        var references = ReferencedNames(InfrastructureAssembly);

        Assert.DoesNotContain(references, n => n == ApiName);
        Assert.DoesNotContain(references, n => n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Api_UsesInfrastructureOnlyForRegistration_AndNeverEfCore()
    {
        var references = ReferencedNames(ApiAssembly);

        Assert.DoesNotContain(references, n => n.StartsWith(EfCore, StringComparison.Ordinal));
    }

    private static string[] ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();
}
