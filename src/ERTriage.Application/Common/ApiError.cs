namespace ERTriage.Application.Common;

/// <summary>A single error entry. <paramref name="Field"/> is null for non-field errors.</summary>
public sealed record ApiError(string? Field, string Message);
