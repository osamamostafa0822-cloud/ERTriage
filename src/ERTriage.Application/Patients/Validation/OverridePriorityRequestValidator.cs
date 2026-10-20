using ERTriage.Application.Common;
using ERTriage.Application.Patients.Requests;
using ERTriage.Domain.Patients;
using ERTriage.Domain.Triage;

namespace ERTriage.Application.Patients.Validation;

/// <summary>
/// Validates a priority override. The reason is mandatory and non-blank (D-05, PDF §02). Whether the patient's
/// status still allows an override (409) is decided by the Domain.
/// </summary>
public static class OverridePriorityRequestValidator
{
    public static IReadOnlyList<ApiError> Validate(OverridePriorityRequest? request)
    {
        if (request is null)
        {
            return [new ApiError(null, "Request body is required.")];
        }

        var errors = new FieldErrors();

        errors.Range("priority", "Priority", request.Priority, TriagePriority.MostUrgent, TriagePriority.LeastUrgent);
        errors.RequiredText("reason", "Override reason", request.Reason, PatientRules.OverrideReasonMinLength, PatientRules.OverrideReasonMaxLength);
        errors.OptionalText("overriddenBy", "Overridden by", request.OverriddenBy, PatientRules.OverriddenByMaxLength);

        return errors.ToList();
    }

    public static void EnsureValid(OverridePriorityRequest? request)
    {
        var errors = Validate(request);

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }
}
