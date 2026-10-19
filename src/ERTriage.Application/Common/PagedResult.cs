namespace ERTriage.Application.Common;

/// <summary>A single page of results plus the metadata needed to navigate the full set.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int PageNumber, int PageSize)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}
