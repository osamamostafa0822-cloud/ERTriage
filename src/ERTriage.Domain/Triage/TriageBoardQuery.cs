using ERTriage.Domain.Patients;

namespace ERTriage.Domain.Triage;

/// <summary>
/// Input of the triage-board read (docs/03-features/triage-board.md). An empty <see cref="Statuses"/> means
/// "no status filter": the default (Waiting + InTreatment) is applied by the calling service, not the repository.
/// Empty or null <see cref="Priorities"/> means "no priority filter"; the filter applies to the current priority
/// (an override counts), so a patient overridden to 1 appears under priority 1.
/// <see cref="Skip"/> and <see cref="Take"/> are already-validated offsets: user paging input (page number, page size,
/// the 1-100 limit) is validated in Application by <c>PaginationRequest</c> before this query is built.
/// </summary>
public sealed record TriageBoardQuery(
    IReadOnlyCollection<PatientStatus> Statuses,
    int Skip,
    int Take,
    IReadOnlyCollection<int>? Priorities = null);
