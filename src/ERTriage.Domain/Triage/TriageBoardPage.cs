namespace ERTriage.Domain.Triage;

/// <summary>
/// Result of <see cref="ITriageBoardReadRepository.GetPageAsync"/>: the requested slice and the total number of rows
/// matching the filters. Page number, page size and total pages are added by Application (<c>PagedResult</c>).
/// </summary>
public sealed record TriageBoardPage(IReadOnlyList<TriageBoardRow> Items, int TotalCount);
