using ERTriage.Domain.Common;
namespace ERTriage.Domain.Common;

/// <summary>
/// A patient status change is not allowed by the transition matrix.
/// Statuses are carried as names so this type does not depend on the status enum (introduced in M2).
/// </summary>
public sealed class InvalidStatusTransitionException : DomainException
{
    public InvalidStatusTransitionException(string from, string to)
        : base(MessageCatalog.Format("InvalidTransition", MessageCatalog.Status(from), MessageCatalog.Status(to)))
    {
        From = from;
        To = to;
    }

    public string From { get; }

    public string To { get; }
}
