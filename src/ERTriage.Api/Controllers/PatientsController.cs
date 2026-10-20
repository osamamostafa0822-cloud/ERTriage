using ERTriage.Application.Common;
using ERTriage.Application.Patients.Dtos;
using ERTriage.Application.Patients.Requests;
using Microsoft.AspNetCore.Mvc;
using ERTriage.Application.Patients.Services;

namespace ERTriage.Api.Controllers;

/// <summary>
/// Patient registration, lookup, status changes and priority overrides (docs/03-features, m4-plan.md section 5).
/// Thin by design: one Application service call per action. Validation, 404 and 409 are raised as exceptions by the
/// services/Domain and turned into <see cref="GeneralResponse{T}"/> by <c>GlobalExceptionHandler</c>; binding errors
/// (malformed JSON, bad enum, bad id) are turned into the same shape by <c>InvalidModelStateResponseFactory</c>.
/// </summary>
[ApiController]
[Route("api/patients")]
[Produces("application/json")]
[ProducesResponseType(typeof(GeneralResponse<object>), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(GeneralResponse<object>), StatusCodes.Status500InternalServerError)]
public sealed class PatientsController(
    IPatientRegistrationService registrationService,
    IPatientWorkflowService workflowService) : ControllerBase
{
    /// <summary>Registers an arriving patient. The priority is calculated by the Domain from the vital signs.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(GeneralResponse<PatientDto>), StatusCodes.Status201Created)]
    public async Task<ActionResult<GeneralResponse<PatientDto>>> Register(
        [FromBody] RegisterPatientRequest request,
        CancellationToken cancellationToken)
    {
        var patient = await registrationService.RegisterAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = patient.Id }, GeneralResponse<PatientDto>.Ok(patient));
    }

    /// <summary>Returns one patient.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(GeneralResponse<PatientDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GeneralResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GeneralResponse<PatientDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var patient = await registrationService.GetByIdAsync(id, cancellationToken);

        return Ok(GeneralResponse<PatientDto>.Ok(patient));
    }

    /// <summary>Moves the patient to a new status. Discharged and Admitted require a disposition note.</summary>
    [HttpPost("{id}/status")]
    [ProducesResponseType(typeof(GeneralResponse<PatientDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GeneralResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GeneralResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GeneralResponse<PatientDto>>> ChangeStatus(
        Guid id,
        [FromBody] ChangePatientStatusRequest request,
        CancellationToken cancellationToken)
    {
        var patient = await workflowService.ChangeStatusAsync(id, request, cancellationToken);

        return Ok(GeneralResponse<PatientDto>.Ok(patient));
    }

    /// <summary>Overrides the calculated priority. A reason is mandatory.</summary>
    [HttpPut("{id}/priority-override")]
    [ProducesResponseType(typeof(GeneralResponse<PatientDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GeneralResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GeneralResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GeneralResponse<PatientDto>>> OverridePriority(
        Guid id,
        [FromBody] OverridePriorityRequest request,
        CancellationToken cancellationToken)
    {
        var patient = await workflowService.OverridePriorityAsync(id, request, cancellationToken);

        return Ok(GeneralResponse<PatientDto>.Ok(patient));
    }
}
