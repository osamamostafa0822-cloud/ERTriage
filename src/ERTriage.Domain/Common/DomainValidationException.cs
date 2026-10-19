namespace ERTriage.Domain.Common;

/// <summary>A domain invariant was violated (for example, an override without a reason).</summary>
public sealed class DomainValidationException : DomainException
{
    public DomainValidationException(string message) : base(message)
    {
    }
}
