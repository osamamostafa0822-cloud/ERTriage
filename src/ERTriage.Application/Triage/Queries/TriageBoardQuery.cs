using ERTriage.Application.Common;
using ERTriage.Domain.Patients;

namespace ERTriage.Application.Triage.Queries;

/// <summary>
/// Input of the triage-board read (docs/03-features/triage-board.md). An empty <see cref="Statuses"/> means
/// "no status filter": the default (Waiting + InTreatment) is applied by the calling service, not the repository.
/// Empty or null <see cref="Priorities"/> means "no priority filter"; the filter applies to the current priority
/// (an override counts), so a patient overridden to 1 appears under priority 1.
/// </summary>
public sealed record TriageBoardQuery(
    IReadOnlyCollection<PatientStatus> Statuses,
    PaginationRequest Page,
    IReadOnlyCollection<int>? Priorities = null);
