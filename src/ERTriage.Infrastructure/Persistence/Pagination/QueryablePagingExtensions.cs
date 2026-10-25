using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace ERTriage.Infrastructure.Persistence.Pagination;

/// <summary>Reusable database-level paging (docs/02-architecture/pagination.md). Internal: <c>IQueryable</c> never leaves Infrastructure.</summary>
internal static class QueryablePagingExtensions
{
    /// <summary>
    /// Counts, then reads one slice with <c>Skip</c>/<c>Take</c> and the <paramref name="selector"/> projection executed
    /// by the database (only the selected columns are read). Takes an <see cref="IOrderedQueryable{T}"/> so a query
    /// without a sort order cannot be paged (the caller must end the order with a unique tie-breaker); the projection
    /// is applied after ordering and paging, so ordering always uses the entity's columns. User paging input is
    /// validated in Application; <paramref name="skip"/> and <paramref name="take"/> are only guarded here against
    /// programming errors. The count and the slice are two queries and are not one snapshot: rows added in between can
    /// make the total and the items disagree by a few rows for one request.
    /// </summary>
    public static async Task<(IReadOnlyList<TResult> Items, int TotalCount)> ToPageAsync<TSource, TResult>(
        this IOrderedQueryable<TSource> orderedQuery,
        Expression<Func<TSource, TResult>> selector,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderedQuery);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);

        // EF drops the ORDER BY from COUNT, so counting the ordered query costs nothing extra.
        var totalCount = await orderedQuery.CountAsync(cancellationToken);

        // Includes the empty set and a slice beyond the last row: no second round trip, empty Items, correct total.
        if (skip >= totalCount)
        {
            return (Array.Empty<TResult>(), totalCount);
        }

        var items = await orderedQuery
            .Skip(skip)
            .Take(take)
            .Select(selector)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
