using ERTriage.Domain.Patients;
using Microsoft.EntityFrameworkCore;

namespace ERTriage.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of the Domain's <see cref="IPatientRepository"/>. Updates use tracked entities; read-only
/// loads use <c>AsNoTracking</c>. Never calls <c>SaveChanges</c> (that is the Unit of Work's job). The board read lives
/// in <see cref="TriageBoardReadRepository"/>.
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
}
