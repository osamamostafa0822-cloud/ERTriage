namespace ERTriage.Application.Triage;

/// <summary>Waiting-time indicator level (docs/03-features/waiting-time.md). <c>None</c> means no indicator.</summary>
public enum WaitingLevel
{
    None,
    Normal,
    Warning,
    Critical
}
