namespace ERTriage.Domain.Common;

/// <summary>
/// A patient status change is not allowed by the transition matrix.
/// Statuses are carried as names so this type does not depend on the status enum (introduced in M2).
/// </summary>
public sealed class InvalidStatusTransitionException : DomainException
{
    public InvalidStatusTransitionException(string from, string to)
        : base($"Cannot change status from {from} to {to}.")
    {
        From = from;
        To = to;
    }

    public string From { get; }

    public string To { get; }
}
