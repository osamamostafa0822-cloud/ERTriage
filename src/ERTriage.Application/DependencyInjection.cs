using Microsoft.Extensions.DependencyInjection;

namespace ERTriage.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers Application-layer services. Use-case services and validators are added here
    /// by the milestones that introduce them (M4); none exist in M1.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
