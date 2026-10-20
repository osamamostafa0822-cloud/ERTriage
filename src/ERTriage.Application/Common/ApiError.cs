using System.Text.Json.Serialization;
using ERTriage.Domain.Common;

namespace ERTriage.Application.Common;

/// <summary>A single error entry. <paramref name="Field"/> is null for non-field errors.</summary>
public sealed record ApiError(string? Field, string Message)
{
    public ApiError(string? field, MessageDescriptor message) : this(field, message.ToString())
    {
        Description = message;
    }

    [JsonIgnore]
    public MessageDescriptor? Description { get; init; }
}
