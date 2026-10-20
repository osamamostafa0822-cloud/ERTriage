using ERTriage.Domain.Common;
namespace ERTriage.Domain.Common;

/// <summary>
/// An operation is not allowed in the patient's current status (for example overriding the priority of a
/// discharged patient). Distinct from <see cref="InvalidStatusTransitionException"/>, which is about status changes.
/// </summary>
public sealed class PatientStatusConflictException : DomainException
{
    public PatientStatusConflictException(string operation, string status)
        : base(MessageCatalog.Format("StatusConflict", operation == "override priority" ? (object)MessageCatalog.Get("OverrideOperation") : operation, MessageCatalog.Status(status)))
    {
        Operation = operation;
        Status = status;
    }

    public string Operation { get; }

    public string Status { get; }
}
