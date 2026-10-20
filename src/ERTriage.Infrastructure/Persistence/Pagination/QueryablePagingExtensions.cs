using ERTriage.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace ERTriage.Infrastructure.Persistence.Pagination;

/// <summary>Reusable database-level paging (docs/02-architecture/pagination.md). Internal: <c>IQueryable</c> never leaves Infrastructure.</summary>
internal static class QueryablePagingExtensions
{
    /// <summary>
    /// Counts, then reads one page with <c>Skip</c>/<c>Take</c> executed by the database. Takes an
    /// <see cref="IOrderedQueryable{T}"/> so a query without a sort order cannot be paged (the caller must end the
    /// order with a unique tie-breaker). The count and the page are two queries and are not one snapshot: rows
    /// added in between can make <c>TotalCount</c> and <c>Items</c> disagree by a few rows for one request.
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IOrderedQueryable<T> orderedQuery,
        PaginationRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderedQuery);
        ArgumentNullException.ThrowIfNull(page);

        page.EnsureValid();

        var totalCount = await orderedQuery.CountAsync(cancellationToken);

        // Includes the empty set and a page beyond the last one: no second round trip, empty Items, correct TotalCount.
        if (page.Skip >= totalCount)
        {
            return new PagedResult<T>([], totalCount, page.PageNumber, page.PageSize);
        }

        var items = await orderedQuery
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, totalCount, page.PageNumber, page.PageSize);
    }
}
