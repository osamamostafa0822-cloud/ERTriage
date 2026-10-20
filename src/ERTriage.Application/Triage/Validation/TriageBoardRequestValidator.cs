using ERTriage.Domain.Common;
using ERTriage.Application.Common;
using ERTriage.Domain.Triage;
using ERTriage.Application.Triage.Requests;

namespace ERTriage.Application.Triage.Validation;

/// <summary>
/// Validates the board query: every status must be a defined value, every priority 1-4 (Q6), and the paging is checked
/// by <see cref="PaginationRequest"/> itself so its rules are not duplicated.
/// </summary>
public static class TriageBoardRequestValidator
{
    public static IReadOnlyList<ApiError> Validate(TriageBoardRequest? request)
    {
        if (request is null)
        {
            return [new ApiError(null, MessageCatalog.Get("RequestRequired"))];
        }

        var errors = new FieldErrors();

        if (request.Statuses is not null && request.Statuses.Any(status => !Enum.IsDefined(status)))
        {
            errors.Add("status", MessageCatalog.Get("StatusInvalid"));
        }

        if (request.Priorities is not null && request.Priorities.Any(priority => !TriagePriority.IsValid(priority)))
        {
            errors.Add(
                "priority",
                MessageCatalog.Format("PriorityRange", TriagePriority.MostUrgent, TriagePriority.LeastUrgent));
        }

        errors.AddRange(new PaginationRequest(request.PageNumber, request.PageSize).GetErrors());

        return errors.ToList();
    }

    public static void EnsureValid(TriageBoardRequest? request)
    {
        var errors = Validate(request);

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }
}
