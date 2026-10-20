using System.Reflection;
using ERTriage.Domain.Patients;

namespace ERTriage.Domain.Common;

/// <summary>Creates message descriptors; never reads request culture or performs translation.</summary>
public static class MessageCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Required"] = "{0} is required.",
        ["TextLength"] = "{0} must be between {1} and {2} characters.",
        ["TextMaxLength"] = "{0} must be at most {1} characters.",
        ["Range"] = "{0} must be between {1} and {2}{3}.",
        ["DecimalRange"] = "{0} must be between {1:0.0} and {2:0.0}{3}.",
        ["FullName"] = "Full name",
        ["Age"] = "Age",
        ["ChiefComplaint"] = "Chief complaint",
        ["HeartRate"] = "Heart rate",
        ["SystolicBp"] = "Systolic blood pressure",
        ["DiastolicBp"] = "Diastolic blood pressure",
        ["Temperature"] = "Temperature",
        ["SpO2"] = "SpO2",
        ["Priority"] = "Priority",
        ["OverrideReason"] = "Override reason",
        ["OverriddenBy"] = "Overridden by",
        ["DispositionNote"] = "Disposition note",
        ["Years"] = " years",
        ["Bpm"] = " bpm",
        ["MmHg"] = " mmHg",
        ["Celsius"] = " °C",
        ["Percent"] = " %",
        ["BodyRequired"] = "Request body is required.",
        ["RequestRequired"] = "Request is required.",
        ["TargetRequired"] = "Target status is required.",
        ["TargetInvalid"] = "Target status is not a valid patient status.",
        ["StatusInvalid"] = "Status must be a valid patient status.",
        ["DispositionNotAllowed"] = "A disposition note is only allowed when discharging or admitting.",
        ["BpOrder"] = "Diastolic blood pressure must be lower than systolic blood pressure.",
        ["TemperaturePrecision"] = "Temperature must have at most one decimal place.",
        ["ArrivalRequired"] = "Arrival time is required.",
        ["ArrivalFuture"] = "Arrival time cannot be more than {0} minutes in the future.",
        ["VitalsRequired"] = "Vital signs are required.",
        ["PriorityRange"] = "Priority must be between {0} and {1}.",
        ["PageNumberMin"] = "Page number must be at least {0}.",
        ["PageSizeRange"] = "Page size must be between 1 and {0}.",
        ["ConcurrencyConflict"] = "The record was modified by another user. Reload and retry.",
        ["ValidationFailed"] = "Validation failed.",
        ["PatientNotFound"] = "Patient was not found.",
        ["InvalidTransition"] = "Cannot change status from {0} to {1}.",
        ["StatusConflict"] = "Cannot {0} while the patient is {1}.",
        ["Waiting"] = "Waiting",
        ["InTreatment"] = "InTreatment",
        ["Discharged"] = "Discharged",
        ["Admitted"] = "Admitted",
        ["OverrideOperation"] = "override priority",
    };

    public static MessageDescriptor Get(string key) => new(key, Templates[key]);

    public static MessageDescriptor Format(string key, params object[] arguments) => new(key, Templates[key], arguments);

    private static readonly IReadOnlyDictionary<string, string> StatusKeys = typeof(PatientStatus)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (field.Name, Attribute: field.GetCustomAttribute<MessageKeyAttribute>()))
        .Where(item => item.Attribute is not null)
        .ToDictionary(item => item.Name, item => item.Attribute!.Key, StringComparer.Ordinal);

    /// <summary>Returns a descriptor for a known status name (key read from <see cref="MessageKeyAttribute"/>); unknown names pass through unchanged.</summary>
    public static object Status(string status) => StatusKeys.TryGetValue(status, out var key) ? Get(key) : status;
}
