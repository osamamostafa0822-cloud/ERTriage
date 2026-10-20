namespace ERTriage.Application.Common;

/// <summary>
/// Reusable pagination input. Out-of-range values are rejected with a <see cref="ValidationException"/>
/// (HTTP 400) rather than silently clamped - see pagination.md.
/// </summary>
public record PaginationRequest(
    int PageNumber = PaginationRequest.FirstPage,
    int PageSize = PaginationRequest.DefaultPageSize)
{
    public const int FirstPage = 1;
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;

    /// <summary>Number of rows to skip. Only meaningful after <see cref="EnsureValid"/> succeeded.</summary>
    public int Skip => (int)Math.Min((long)(PageNumber - 1) * PageSize, int.MaxValue);

    /// <summary>The validation errors of this request (empty when valid). Lets validators collect them with other fields' errors.</summary>
    public IReadOnlyList<ApiError> GetErrors()
    {
        List<ApiError> errors = [];

        if (PageNumber < FirstPage)
        {
            errors.Add(new ApiError("pageNumber", $"Page number must be at least {FirstPage}."));
        }

        if (PageSize < 1 || PageSize > MaxPageSize)
        {
            errors.Add(new ApiError("pageSize", $"Page size must be between 1 and {MaxPageSize}."));
        }

        return errors;
    }

    public void EnsureValid()
    {
        var errors = GetErrors();

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }
}
