namespace ERTriage.Domain.Common;

/// <summary>
/// Marks an enum member with the stable message key used to display it.
/// Carries the key only; translation happens in the API layer, so Domain stays free of resources and culture.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class MessageKeyAttribute : Attribute
{
    public MessageKeyAttribute(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
    }

    public string Key { get; }
}
