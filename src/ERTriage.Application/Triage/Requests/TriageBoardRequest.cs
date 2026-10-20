using ERTriage.Application.Common;
using ERTriage.Domain.Patients;

namespace ERTriage.Application.Triage.Requests;

/// <summary>
/// Query of <c>GET /api/triage-board</c>. No <c>Statuses</c> means the default Waiting + InTreatment (applied by the
/// board service in P2, Q7); no <c>Priorities</c> means every priority. Out-of-range paging is rejected, never clamped (D-23).
/// </summary>
public sealed record TriageBoardRequest(
    IReadOnlyList<PatientStatus>? Statuses = null,
    IReadOnlyList<int>? Priorities = null,
    int PageNumber = PaginationRequest.FirstPage,
    int PageSize = PaginationRequest.DefaultPageSize);
