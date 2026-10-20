using System.Globalization;

namespace ERTriage.Domain.Common;

/// <summary>Language-independent message code, arguments, and an English diagnostic fallback.</summary>
public sealed record MessageDescriptor(string Key, string Template, params object[] Arguments)
{
    public override string ToString() => string.Format(CultureInfo.InvariantCulture, Template,
        Arguments.Select(argument => argument is MessageDescriptor nested ? nested.ToString() : argument).ToArray());

    public bool Equals(MessageDescriptor? other) =>
        other is not null && Key == other.Key && Template == other.Template
        && Arguments.SequenceEqual(other.Arguments);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Key);
        hash.Add(Template);
        foreach (var argument in Arguments) hash.Add(argument);
        return hash.ToHashCode();
    }
}
