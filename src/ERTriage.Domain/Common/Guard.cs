using System.Globalization;

namespace ERTriage.Domain.Common;

/// <summary>Small, shared invariant checks. Messages are client-safe and never echo the supplied value.</summary>
internal static class Guard
{
    /// <summary>Trims <paramref name="value"/>; it must be non-empty and within the length bounds.</summary>
    public static string RequiredText(string? value, string label, int minLength, int maxLength)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainValidationException($"{label} is required.");
        }

        if (trimmed.Length < minLength || trimmed.Length > maxLength)
        {
            throw new DomainValidationException(
                FormattableString.Invariant($"{label} must be between {minLength} and {maxLength} characters."));
        }

        return trimmed;
    }

    /// <summary>Trims <paramref name="value"/>; blank becomes null; otherwise it must fit <paramref name="maxLength"/>.</summary>
    public static string? OptionalText(string? value, string label, int maxLength)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainValidationException(
                FormattableString.Invariant($"{label} must be at most {maxLength} characters."));
        }

        return trimmed;
    }

    public static void InRange(int value, int min, int max, string label, string unit = "")
    {
        if (value < min || value > max)
        {
            throw new DomainValidationException(
                FormattableString.Invariant($"{label} must be between {min} and {max}{unit}."));
        }
    }

    public static void InRange(decimal value, decimal min, decimal max, string label, string unit = "")
    {
        if (value < min || value > max)
        {
            throw new DomainValidationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label} must be between {min:0.0} and {max:0.0}{unit}."));
        }
    }
}
