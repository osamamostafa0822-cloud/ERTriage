using ERTriage.Domain.Common;

namespace ERTriage.Domain.Common;

/// <summary>Small, shared invariant checks. Messages are client-safe and never echo the supplied value.</summary>
internal static class Guard
{
    /// <summary>Trims <paramref name="value"/>; it must be non-empty and within the length bounds.</summary>
    public static string RequiredText(string? value, MessageDescriptor label, int minLength, int maxLength)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainValidationException(MessageCatalog.Format("Required", label));
        }

        if (trimmed.Length < minLength || trimmed.Length > maxLength)
        {
            throw new DomainValidationException(
                MessageCatalog.Format("TextLength", label, minLength, maxLength));
        }

        return trimmed;
    }

    /// <summary>Trims <paramref name="value"/>; blank becomes null; otherwise it must fit <paramref name="maxLength"/>.</summary>
    public static string? OptionalText(string? value, MessageDescriptor label, int maxLength)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainValidationException(
                MessageCatalog.Format("TextMaxLength", label, maxLength));
        }

        return trimmed;
    }

    public static void InRange(int value, int min, int max, MessageDescriptor label, MessageDescriptor? unit = null)
    {
        if (value < min || value > max)
        {
            throw new DomainValidationException(
                MessageCatalog.Format("Range", label, min, max, (object?)unit ?? ""));
        }
    }

    public static void InRange(decimal value, decimal min, decimal max, MessageDescriptor label, MessageDescriptor? unit = null)
    {
        if (value < min || value > max)
        {
            throw new DomainValidationException(
                MessageCatalog.Format("DecimalRange", label, min, max, (object?)unit ?? ""));
        }
    }
}
