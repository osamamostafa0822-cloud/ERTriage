using ERTriage.Application.Common;
using ERTriage.Application.Triage.Dtos;
using ERTriage.Application.Triage.Validation;
using ERTriage.Domain.Patients;
using Microsoft.Extensions.Logging;
using ERTriage.Application.Patients.Repositories;
using ERTriage.Application.Triage.Mapping;
using ERTriage.Application.Triage.Queries;
using ERTriage.Application.Triage.Requests;

namespace ERTriage.Application.Triage.Services;

/// <summary>
/// Reads one board page. Filtering, ordering (priority, arrival time, Id) and paging are done by the repository in
/// the database; this service validates the query, applies the approved default status filter (Q7) and adds the
/// waiting-time indicator using a single clock reading for the whole page.
/// </summary>
public sealed class TriageBoardService : ITriageBoardService
{
    /// <summary>Statuses shown when the caller sends none: the active queue (Waiting + InTreatment).</summary>
    public static readonly IReadOnlyList<PatientStatus> DefaultStatuses = [PatientStatus.Waiting, PatientStatus.InTreatment];

    private readonly IPatientRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TriageBoardService> _logger;

    public TriageBoardService(IPatientRepository repository, TimeProvider timeProvider, ILogger<TriageBoardService> logger)
    {
        _logger = logger;
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<PagedResult<TriageBoardItemDto>> GetBoardAsync(
        TriageBoardRequest? request,
        CancellationToken cancellationToken)
    {
        TriageBoardRequestValidator.EnsureValid(request);
        var valid = request!;

        var statuses = valid.Statuses?.Distinct().ToArray() is { Length: > 0 } requested
            ? requested
            : DefaultStatuses.ToArray();
        var priorities = valid.Priorities?.Distinct().ToArray() ?? [];

        var query = new TriageBoardQuery(statuses, new PaginationRequest(valid.PageNumber, valid.PageSize), priorities);
        var page = await _repository.GetBoardPageAsync(query, cancellationToken);

        // Debug, not Information: the board is polled often. Counts only, no patient data.
        _logger.LogDebug(
            "Triage board page read. Page {PageNumber}, Size {PageSize}, Returned {ReturnedCount}, Total {TotalCount}",
            page.PageNumber,
            page.PageSize,
            page.Items.Count,
            page.TotalCount);

        var now = _timeProvider.GetUtcNow();
        var items = page.Items.Select(patient => TriageBoardMapper.ToItem(patient, now)).ToList();

        return new PagedResult<TriageBoardItemDto>(items, page.TotalCount, page.PageNumber, page.PageSize);
    }
}
