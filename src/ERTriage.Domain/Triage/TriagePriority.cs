namespace ERTriage.Domain.Triage;

/// <summary>Priority scale: 1 is the most urgent, 4 the least urgent.</summary>
public static class TriagePriority
{
    public const int MostUrgent = 1;
    public const int LeastUrgent = 4;

    public static bool IsValid(int priority) => priority is >= MostUrgent and <= LeastUrgent;
}
