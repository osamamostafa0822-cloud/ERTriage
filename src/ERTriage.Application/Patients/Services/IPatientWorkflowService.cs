using ERTriage.Application.Patients.Dtos;
using ERTriage.Application.Patients.Requests;

namespace ERTriage.Application.Patients.Services;

/// <summary>
/// Changes to an existing patient: status transitions and priority overrides (D-06). Both load the patient tracked,
/// call the Domain, save once and return the updated patient. 400 validation, 404 unknown id, 409 invalid transition,
/// override not allowed in the current status, or concurrency conflict.
/// </summary>
public interface IPatientWorkflowService
{
    Task<PatientDto> ChangeStatusAsync(Guid id, ChangePatientStatusRequest? request, CancellationToken cancellationToken);

    Task<PatientDto> OverridePriorityAsync(Guid id, OverridePriorityRequest? request, CancellationToken cancellationToken);
}
