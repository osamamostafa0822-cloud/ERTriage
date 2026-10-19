using ERTriage.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERTriage.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF Core migrations. Creates the database when it does not exist yet. Failures propagate so the
/// host does not start on a half-migrated database; they are logged once by the host, not here.
/// </summary>
internal sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly ErTriageDbContext _context;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(ErTriageDbContext context, ILogger<DatabaseInitializer> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var pending = (await _context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pending.Count == 0)
        {
            _logger.LogInformation("Database is up to date; no pending migrations");
            return;
        }

        _logger.LogInformation(
            "Applying {PendingMigrationCount} pending database migration(s): {PendingMigrations}",
            pending.Count,
            string.Join(", ", pending));

        await _context.Database.MigrateAsync(cancellationToken);

        _logger.LogInformation("Database migrations applied");
    }
}
