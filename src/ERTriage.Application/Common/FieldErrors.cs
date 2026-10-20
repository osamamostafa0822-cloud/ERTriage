using System.Globalization;

namespace ERTriage.Application.Common;

/// <summary>
/// Collects field errors so a validator can report every problem at once. Messages mirror the Domain's wording,
/// are client-safe and never echo the supplied value. The Domain stays the final authority for invariants.
/// </summary>
internal sealed class FieldErrors
{
    private readonly List<ApiError> _errors = [];

    public void Add(string? field, string message) => _errors.Add(new ApiError(field, message));

    public void AddRange(IEnumerable<ApiError> errors) => _errors.AddRange(errors);

    public IReadOnlyList<ApiError> ToList() => _errors.ToArray();

    /// <summary>Required text, trimmed, within the bounds. Returns false (and adds an error) otherwise.</summary>
    public bool RequiredText(string field, string label, string? value, int minLength, int maxLength)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            Add(field, $"{label} is required.");
            return false;
        }

        if (trimmed.Length < minLength || trimmed.Length > maxLength)
        {
            Add(field, FormattableString.Invariant($"{label} must be between {minLength} and {maxLength} characters."));
            return false;
        }

        return true;
    }

    /// <summary>Optional text: blank is fine; otherwise (trimmed) it must fit <paramref name="maxLength"/>.</summary>
    public bool OptionalText(string field, string label, string? value, int maxLength)
    {
        var trimmed = value?.Trim();

        if (!string.IsNullOrEmpty(trimmed) && trimmed.Length > maxLength)
        {
            Add(field, FormattableString.Invariant($"{label} must be at most {maxLength} characters."));
            return false;
        }

        return true;
    }

    public bool Range(string field, string label, int? value, int min, int max, string unit = "")
    {
        if (value is not { } number)
        {
            Add(field, $"{label} is required.");
            return false;
        }

        if (number < min || number > max)
        {
            Add(field, FormattableString.Invariant($"{label} must be between {min} and {max}{unit}."));
            return false;
        }

        return true;
    }

    public bool Range(string field, string label, decimal? value, decimal min, decimal max, string unit = "")
    {
        if (value is not { } number)
        {
            Add(field, $"{label} is required.");
            return false;
        }

        if (number < min || number > max)
        {
            Add(field, string.Create(CultureInfo.InvariantCulture, $"{label} must be between {min:0.0} and {max:0.0}{unit}."));
            return false;
        }

        return true;
    }
}
