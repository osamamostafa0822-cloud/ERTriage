namespace ERTriage.Domain.Common;

/// <summary>
/// Base type for business-rule violations raised by the Domain.
/// Domain has no HTTP knowledge; the Api layer maps these to status codes.
/// Messages must be safe to show to clients and must not contain patient data.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message)
    {
    }
}
