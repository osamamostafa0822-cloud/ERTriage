namespace ERTriage.Application.Patients.Notifications;

/// <summary>What happened to a patient. Wire values are the enum names (the Api serializes enums as strings).</summary>
public enum PatientChangeType
{
    Registered,
    StatusChanged,
    PriorityOverridden
}
