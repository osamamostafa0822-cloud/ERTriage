using ERTriage.Application.Common;
using ERTriage.Application.Triage.Dtos;

namespace ERTriage.Application.Triage;

/// <summary>The prioritized queue (docs/03-features/triage-board.md). Throws <c>ValidationException</c> (400) for a bad filter or page.</summary>
public interface ITriageBoardService
{
    Task<PagedResult<TriageBoardItemDto>> GetBoardAsync(TriageBoardRequest? request, CancellationToken cancellationToken);
}
