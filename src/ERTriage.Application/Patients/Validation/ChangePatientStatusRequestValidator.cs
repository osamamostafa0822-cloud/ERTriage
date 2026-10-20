using ERTriage.Application.Common;
using ERTriage.Application.Patients.Requests;
using ERTriage.Domain.Patients;

namespace ERTriage.Application.Patients.Validation;

/// <summary>
/// Validates the shape of a status change. Whether the transition itself is allowed is the Domain's decision
/// (<c>PatientStatusTransitions</c>, HTTP 409), so a Waiting target is not judged here: the Domain reports an invalid
/// transition before looking at the note.
/// </summary>
public static class ChangePatientStatusRequestValidator
{
    public static IReadOnlyList<ApiError> Validate(ChangePatientStatusRequest? request)
    {
        if (request is null)
        {
            return [new ApiError(null, "Request body is required.")];
        }

        var errors = new FieldErrors();

        if (request.TargetStatus is not { } target)
        {
            errors.Add("targetStatus", "Target status is required.");
            return errors.ToList();
        }

        if (!Enum.IsDefined(target))
        {
            errors.Add("targetStatus", "Target status is not a valid patient status.");
            return errors.ToList();
        }

        switch (target)
        {
            case PatientStatus.Discharged:
            case PatientStatus.Admitted:
                errors.RequiredText(
                    "dispositionNote",
                    "Disposition note",
                    request.DispositionNote,
                    PatientRules.DispositionNoteMinLength,
                    PatientRules.DispositionNoteMaxLength);
                break;

            case PatientStatus.InTreatment when !string.IsNullOrWhiteSpace(request.DispositionNote):
                errors.Add("dispositionNote", "A disposition note is only allowed when discharging or admitting.");
                break;
        }

        return errors.ToList();
    }

    public static void EnsureValid(ChangePatientStatusRequest? request)
    {
        var errors = Validate(request);

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }
}
