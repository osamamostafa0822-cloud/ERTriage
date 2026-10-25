namespace ERTriage.Domain.Common;

/// <summary>
/// Coordinates persistence of a use case. Owned by the Domain next to the repository ports and implemented in
/// Infrastructure. Repositories never save; services call this once after the domain behaviour succeeded.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
