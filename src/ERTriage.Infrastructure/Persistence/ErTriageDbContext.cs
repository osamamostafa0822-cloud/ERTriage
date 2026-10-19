using ERTriage.Domain.Patients;
using ERTriage.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;

namespace ERTriage.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the ER Triage database. Entity mappings live in <c>Persistence/Configurations</c>
/// (one <c>IEntityTypeConfiguration&lt;T&gt;</c> per entity) and are discovered from this assembly.
/// </summary>
public sealed class ErTriageDbContext : DbContext
{
    public ErTriageDbContext(DbContextOptions<ErTriageDbContext> options) : base(options)
    {
    }

    public DbSet<Patient> Patients => Set<Patient>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All Domain timestamps are UTC: read them back with Kind = Utc (database-design.md).
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ErTriageDbContext).Assembly);
    }
}
