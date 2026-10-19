namespace ERTriage.Application.Common;

/// <summary>
/// Coordinates persistence of a use case. Repositories never save; services call this once
/// after the domain behaviour succeeded.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
