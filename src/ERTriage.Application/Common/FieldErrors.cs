using ERTriage.Domain.Common;

namespace ERTriage.Application.Common;

/// <summary>
/// Collects field errors so a validator can report every problem at once. Messages mirror the Domain's wording,
/// are client-safe and never echo the supplied value. The Domain stays the final authority for invariants.
/// </summary>
internal sealed class FieldErrors
{
    private readonly List<ApiError> _errors = [];

    public void Add(string? field, MessageDescriptor message) => _errors.Add(new ApiError(field, message));

    public void AddRange(IEnumerable<ApiError> errors) => _errors.AddRange(errors);

    public IReadOnlyList<ApiError> ToList() => _errors.ToArray();

    /// <summary>Required text, trimmed, within the bounds. Returns false (and adds an error) otherwise.</summary>
    public bool RequiredText(string field, MessageDescriptor label, string? value, int minLength, int maxLength)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            Add(field, MessageCatalog.Format("Required", label));
            return false;
        }

        if (trimmed.Length < minLength || trimmed.Length > maxLength)
        {
            Add(field, MessageCatalog.Format("TextLength", label, minLength, maxLength));
            return false;
        }

        return true;
    }

    /// <summary>Optional text: blank is fine; otherwise (trimmed) it must fit <paramref name="maxLength"/>.</summary>
    public bool OptionalText(string field, MessageDescriptor label, string? value, int maxLength)
    {
        var trimmed = value?.Trim();

        if (!string.IsNullOrEmpty(trimmed) && trimmed.Length > maxLength)
        {
            Add(field, MessageCatalog.Format("TextMaxLength", label, maxLength));
            return false;
        }

        return true;
    }

    public bool Range(string field, MessageDescriptor label, int? value, int min, int max, MessageDescriptor? unit = null)
    {
        if (value is not { } number)
        {
            Add(field, MessageCatalog.Format("Required", label));
            return false;
        }

        if (number < min || number > max)
        {
            Add(field, MessageCatalog.Format("Range", label, min, max, (object?)unit ?? ""));
            return false;
        }

        return true;
    }

    public bool Range(string field, MessageDescriptor label, decimal? value, decimal min, decimal max, MessageDescriptor? unit = null)
    {
        if (value is not { } number)
        {
            Add(field, MessageCatalog.Format("Required", label));
            return false;
        }

        if (number < min || number > max)
        {
            Add(field, MessageCatalog.Format("DecimalRange", label, min, max, (object?)unit ?? ""));
            return false;
        }

        return true;
    }
}
