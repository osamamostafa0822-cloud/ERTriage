namespace ERTriage.Domain.Triage;

/// <summary>Output of the priority calculation: the priority and the human-readable reason.</summary>
public sealed record TriageAssessment(int Priority, string Reason);
