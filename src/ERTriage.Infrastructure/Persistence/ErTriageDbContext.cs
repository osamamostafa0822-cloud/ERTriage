using Microsoft.EntityFrameworkCore;

namespace ERTriage.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the ER Triage database. Entity sets and configurations are added
/// by the persistence milestone (M3); M1 only provides the foundation.
/// </summary>
public sealed class ErTriageDbContext : DbContext
{
    public ErTriageDbContext(DbContextOptions<ErTriageDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ErTriageDbContext).Assembly);
    }
}
