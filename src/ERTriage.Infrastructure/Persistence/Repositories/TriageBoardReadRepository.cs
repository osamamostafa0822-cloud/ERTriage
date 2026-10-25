using System.Linq.Expressions;
using ERTriage.Domain.Patients;
using ERTriage.Domain.Triage;
using ERTriage.Infrastructure.Persistence.Pagination;
using Microsoft.EntityFrameworkCore;

namespace ERTriage.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of the Domain's <see cref="ITriageBoardReadRepository"/>. Filtering, ordering, paging and
/// the projection all run in the database; nothing is tracked.
/// </summary>
internal sealed class TriageBoardReadRepository : ITriageBoardReadRepository
{
    // Only the board's columns. IsOverridden is not mapped (derived in the Domain), so it is projected from its source column.
    private static readonly Expression<Func<Patient, TriageBoardRow>> ToRow = p => new TriageBoardRow(
        p.Id,
        p.FullName,
        p.Age,
        p.ChiefComplaint,
        p.ArrivalTime,
        p.CurrentPriority,
        p.CalculatedPriority,
        p.PriorityReason,
        p.OverridePriority != null,
        p.OverrideReason,
        p.Status,
        p.TreatmentStartedAt);

    private readonly ErTriageDbContext _context;

    public TriageBoardReadRepository(ErTriageDbContext context)
    {
        _context = context;
    }

    public async Task<TriageBoardPage> GetPageAsync(TriageBoardQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Statuses);

        // A List (instance Contains) keeps the predicate unambiguous for EF's expression translation.
        var statuses = query.Statuses.ToList();
        var priorities = query.Priorities?.ToList() ?? [];

        IQueryable<Patient> patients = _context.Patients.AsNoTracking();

        if (statuses.Count > 0)
        {
            patients = patients.Where(p => statuses.Contains(p.Status));
        }

        if (priorities.Count > 0)
        {
            patients = patients.Where(p => priorities.Contains(p.CurrentPriority));
        }

        // Business order (docs/03-features/triage-board.md); Id is the deterministic tie-breaker.
        var ordered = patients
            .OrderBy(p => p.CurrentPriority)
            .ThenBy(p => p.ArrivalTime)
            .ThenBy(p => p.Id);

        var (items, totalCount) = await ordered.ToPageAsync(ToRow, query.Skip, query.Take, cancellationToken);

        return new TriageBoardPage(items, totalCount);
    }
}
