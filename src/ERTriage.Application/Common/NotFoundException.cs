namespace ERTriage.Application.Common;

/// <summary>A requested resource does not exist (HTTP 404). The message must be safe to show to clients.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}
