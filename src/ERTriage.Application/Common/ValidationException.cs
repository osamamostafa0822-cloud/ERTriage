using ERTriage.Domain.Common;
namespace ERTriage.Application.Common;

/// <summary>Request validation failed. Expected client error (HTTP 400), carries field-level errors.</summary>
public sealed class ValidationException : Exception
{
    public ValidationException(IReadOnlyList<ApiError> errors) : base(MessageCatalog.Get("ValidationFailed").ToString())
    {
        Errors = errors;
    }

    public ValidationException(string field, string message) : this([new ApiError(field, message)])
    {
    }

    public IReadOnlyList<ApiError> Errors { get; }
}
