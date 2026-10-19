using ERTriage.Application.Common;
using ERTriage.Domain.Patients;

namespace ERTriage.Application.Triage;

/// <summary>
/// Input of the triage-board read (docs/03-features/triage-board.md). An empty <see cref="Statuses"/> means
/// "no status filter": the default (Waiting + InTreatment) is applied by the calling service, not the repository.
/// The priority filter is deferred to M4.
/// </summary>
public sealed record TriageBoardQuery(IReadOnlyCollection<PatientStatus> Statuses, PaginationRequest Page);
