using ERTriage.Application.Common;
using ERTriage.Application.Triage;
using ERTriage.Domain.Patients;

namespace ERTriage.Application.Patients;

/// <summary>
/// Persistence port of the <see cref="Patient"/> aggregate (docs/02-architecture/repository-and-unit-of-work.md).
/// Specific to this aggregate: no generic repository, no <c>IQueryable</c> or EF type in the contract, no business
/// rules, and it never saves - services call <see cref="IUnitOfWork.SaveChangesAsync"/> once per use case.
/// </summary>
public interface IPatientRepository
{
    /// <summary>
    /// Starts tracking a new patient. Synchronous on purpose: the Domain assigns the Id, so nothing touches the
    /// database until <see cref="IUnitOfWork.SaveChangesAsync"/>.
    /// </summary>
    void Add(Patient patient);

    /// <summary>Loads a patient for update (tracked), or <c>null</c> if it does not exist.</summary>
    Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Loads a patient for display only (not tracked), or <c>null</c> if it does not exist.</summary>
    Task<Patient?> GetByIdReadOnlyAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the triage board (not tracked): filtered by status, ordered by current priority, arrival time
    /// and Id, paged in the database.
    /// </summary>
    Task<PagedResult<Patient>> GetBoardPageAsync(TriageBoardQuery query, CancellationToken cancellationToken);
}
