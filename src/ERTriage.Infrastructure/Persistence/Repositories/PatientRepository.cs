using ERTriage.Application.Common;
using ERTriage.Domain.Patients;
using ERTriage.Infrastructure.Persistence.Pagination;
using Microsoft.EntityFrameworkCore;
using ERTriage.Application.Patients.Repositories;
using ERTriage.Application.Triage.Queries;

namespace ERTriage.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPatientRepository"/>. Updates use tracked entities; every read-only query
/// uses <c>AsNoTracking</c>. Never calls <c>SaveChanges</c> (that is the Unit of Work's job).
/// </summary>
internal sealed class PatientRepository : IPatientRepository
{
    private readonly ErTriageDbContext _context;

    public PatientRepository(ErTriageDbContext context)
    {
        _context = context;
    }

    public void Add(Patient patient)
    {
        ArgumentNullException.ThrowIfNull(patient);

        _context.Patients.Add(patient);
    }

    public Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _context.Patients.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Patient?> GetByIdReadOnlyAsync(Guid id, CancellationToken cancellationToken) =>
        _context.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<PagedResult<Patient>> GetBoardPageAsync(TriageBoardQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Statuses);
        ArgumentNullException.ThrowIfNull(query.Page);

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

        return ordered.ToPagedResultAsync(query.Page, cancellationToken);
    }
}
