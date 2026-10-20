using ERTriage.Application.Common;
using ERTriage.Application.Patients;
using ERTriage.Infrastructure.Persistence;
using ERTriage.Infrastructure.Persistence.Repositories;
using ERTriage.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ERTriage.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "ErTriage";

    /// <summary>
    /// Registers persistence: DbContext, Unit of Work and the specific repositories (all Scoped, so they share the
    /// request's DbContext).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Fail fast at startup; the message intentionally never includes connection details.
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured.");
        }

        // AddDbContext registers ErTriageDbContext as Scoped. Sensitive data logging is never enabled.
        services.AddDbContext<ErTriageDbContext>(options => options.UseSqlServer(connectionString));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();

        // Development seed (D-44, D-58). Registered everywhere; it only runs when Program.cs gates it in Development.
        // TryAdd: tests may register their own options (short lock timeout, custom data file) first.
        services.TryAddSingleton(new DevelopmentSeedOptions());
        services.AddScoped<IDevelopmentDataSeeder, DevelopmentDataSeeder>();

        return services;
    }
}
