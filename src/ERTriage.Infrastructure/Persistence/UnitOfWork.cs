using ERTriage.Application.Common;
using Microsoft.EntityFrameworkCore;
using ERTriage.Domain.Common;

namespace ERTriage.Infrastructure.Persistence;

internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly ErTriageDbContext _context;

    public UnitOfWork(ErTriageDbContext context)
    {
        _context = context;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Translate so Application/Api never depend on EF Core exception types.
            throw new ConcurrencyConflictException(ex);
        }
    }
}
