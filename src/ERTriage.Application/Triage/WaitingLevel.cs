using ERTriage.Domain.Common;

namespace ERTriage.Application.Triage;

/// <summary>Waiting-time indicator level (docs/03-features/waiting-time.md). <c>None</c> means no indicator, so it has no display label.</summary>
public enum WaitingLevel
{
    None,
    [MessageKey("Normal")] Normal,
    [MessageKey("Warning")] Warning,
    [MessageKey("Critical")] Critical
}
