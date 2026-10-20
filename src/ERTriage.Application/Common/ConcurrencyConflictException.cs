using ERTriage.Domain.Common;
namespace ERTriage.Application.Common;

/// <summary>
/// The record was modified by someone else since it was loaded (HTTP 409).
/// Raised by the Unit of Work implementation so upper layers never see EF Core exception types.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(Exception innerException)
        : base(MessageCatalog.Get("ConcurrencyConflict").ToString(), innerException)
    {
    }
}
