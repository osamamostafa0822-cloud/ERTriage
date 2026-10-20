using ERTriage.Domain.Common;
using Microsoft.Extensions.Localization;

namespace ERTriage.Api.Localization;

/// <summary>Resolves structured messages at the HTTP boundary, including nested field labels.</summary>
public sealed class MessageLocalizer(IStringLocalizer<Messages> localizer)
{
    public string Get(string key) => localizer[key].Value;

    public string Localize(MessageDescriptor description)
    {
        var arguments = description.Arguments
            .Select(argument => argument is MessageDescriptor nested ? Localize(nested) : argument)
            .ToArray();
        var result = localizer[description.Key, arguments];
        // A missing translation must not turn a client error into a server error.
        return result.ResourceNotFound ? description.ToString() : result.Value;
    }
}
