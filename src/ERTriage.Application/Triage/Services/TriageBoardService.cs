using ERTriage.Application.Common;
using ERTriage.Application.Triage.Dtos;
using ERTriage.Application.Triage.Validation;
using ERTriage.Domain.Patients;
using ERTriage.Domain.Triage;
using Microsoft.Extensions.Logging;
using ERTriage.Application.Triage.Mapping;
using ERTriage.Application.Triage.Requests;

namespace ERTriage.Application.Triage.Services;

/// <summary>
/// Reads one board page. Filtering, ordering (priority, arrival time, Id), paging and the column projection are done
/// by the Domain's board read port in the database; this service validates the query (including paging), applies the
/// approved default status filter (Q7), turns the page into Skip/Take, and adds the waiting-time indicator using a
/// single clock reading for the whole page.
/// </summary>
public sealed class TriageBoardService : ITriageBoardService
{
    /// <summary>Statuses shown when the caller sends none: the active queue (Waiting + InTreatment).</summary>
    public static readonly IReadOnlyList<PatientStatus> DefaultStatuses = [PatientStatus.Waiting, PatientStatus.InTreatment];

    private readonly ITriageBoardReadRepository _boardRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TriageBoardService> _logger;

    public TriageBoardService(
        ITriageBoardReadRepository boardRepository,
        TimeProvider timeProvider,
        ILogger<TriageBoardService> logger)
    {
        _logger = logger;
        _boardRepository = boardRepository;
        _timeProvider = timeProvider;
    }

    public async Task<PagedResult<TriageBoardItemDto>> GetBoardAsync(
        TriageBoardRequest? request,
        CancellationToken cancellationToken)
    {
        // Validates statuses, priorities and paging (PaginationRequest rules) before anything reaches the repository.
        TriageBoardRequestValidator.EnsureValid(request);
        var valid = request!;

        var statuses = valid.Statuses?.Distinct().ToArray() is { Length: > 0 } requested
            ? requested
            : DefaultStatuses.ToArray();
        var priorities = valid.Priorities?.Distinct().ToArray() ?? [];
        var paging = new PaginationRequest(valid.PageNumber, valid.PageSize);

        var query = new TriageBoardQuery(statuses, paging.Skip, paging.PageSize, priorities);
        var page = await _boardRepository.GetPageAsync(query, cancellationToken);

        // Debug, not Information: the board is polled often. Counts only, no patient data.
        _logger.LogDebug(
            "Triage board page read. Page {PageNumber}, Size {PageSize}, Returned {ReturnedCount}, Total {TotalCount}",
            paging.PageNumber,
            paging.PageSize,
            page.Items.Count,
            page.TotalCount);

        var now = _timeProvider.GetUtcNow();
        var items = page.Items.Select(row => TriageBoardMapper.ToItem(row, now)).ToList();

        return new PagedResult<TriageBoardItemDto>(items, page.TotalCount, paging.PageNumber, paging.PageSize);
    }
}
