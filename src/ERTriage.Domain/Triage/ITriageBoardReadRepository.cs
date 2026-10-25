namespace ERTriage.Domain.Triage;

/// <summary>
/// Read port of the triage board (docs/03-features/triage-board.md), owned by the Domain next to the write-side
/// <c>IPatientRepository</c> and implemented in Infrastructure. The contract uses Domain types only: the API's paging
/// contracts (<c>PaginationRequest</c>, <c>PagedResult</c>) and their validation stay in Application. No
/// <c>IQueryable</c> or EF type in the contract.
/// </summary>
public interface ITriageBoardReadRepository
{
    /// <summary>
    /// One slice of the board (not tracked): filtered by status and current priority, ordered by current priority,
    /// arrival time and Id, then skipped, taken and projected to <see cref="TriageBoardRow"/> in the database.
    /// </summary>
    Task<TriageBoardPage> GetPageAsync(TriageBoardQuery query, CancellationToken cancellationToken);
}
