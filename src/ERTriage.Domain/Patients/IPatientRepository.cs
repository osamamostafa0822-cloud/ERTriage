namespace ERTriage.Domain.Patients;

/// <summary>
/// Persistence port of the <see cref="Patient"/> aggregate, owned by the Domain (DDD style) and implemented in
/// Infrastructure (docs/02-architecture/repository-and-unit-of-work.md). Write-side only: load and add the aggregate.
/// Specific to this aggregate: no generic repository, no <c>IQueryable</c> or EF type in the contract, no business
/// rules, and it never saves - services call <see cref="Common.IUnitOfWork.SaveChangesAsync"/> once per use case.
/// Read models (the triage board) use their own Application read port, so no paging or DTO contract leaks in here.
/// </summary>
public interface IPatientRepository
{
    /// <summary>
    /// Starts tracking a new patient. Synchronous on purpose: the Domain assigns the Id, so nothing touches the
    /// database until <see cref="Common.IUnitOfWork.SaveChangesAsync"/>.
    /// </summary>
    void Add(Patient patient);

    /// <summary>Loads a patient for update (tracked), or <c>null</c> if it does not exist.</summary>
    Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Loads a patient for display only (not tracked), or <c>null</c> if it does not exist.</summary>
    Task<Patient?> GetByIdReadOnlyAsync(Guid id, CancellationToken cancellationToken);
}
