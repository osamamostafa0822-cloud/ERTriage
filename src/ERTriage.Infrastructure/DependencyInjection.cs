using ERTriage.Application.Common;
using ERTriage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERTriage.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "ErTriage";

    /// <summary>
    /// Registers persistence: DbContext (Scoped), Unit of Work (Scoped).
    /// Specific repositories are added by the milestone that introduces them (M3).
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

        return services;
    }
}
