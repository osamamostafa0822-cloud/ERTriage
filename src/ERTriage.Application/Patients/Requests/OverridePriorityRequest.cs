namespace ERTriage.Application.Patients.Requests;

/// <summary>Body of <c>PUT /api/patients/{id}/priority-override</c>. A reason is mandatory (D-05).</summary>
public sealed record OverridePriorityRequest(int? Priority, string? Reason, string? OverriddenBy);
