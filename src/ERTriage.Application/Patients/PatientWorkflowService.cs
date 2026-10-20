using ERTriage.Domain.Common;
using ERTriage.Application.Common;
using ERTriage.Application.Patients.Dtos;
using ERTriage.Application.Patients.Requests;
using ERTriage.Application.Patients.Validation;
using ERTriage.Domain.Patients;
using Microsoft.Extensions.Logging;

namespace ERTriage.Application.Patients;

/// <summary>
/// Status change and priority override. The request is validated first (400), then the patient is loaded (404);
/// the Domain decides whether the operation is allowed (409). Optimistic concurrency is handled by the Unit of Work
/// (<see cref="ConcurrencyConflictException"/>, D-22/D-42); nothing here swallows or rewrites those exceptions.
/// </summary>
public sealed class PatientWorkflowService : IPatientWorkflowService
{
    private readonly IPatientRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PatientWorkflowService> _logger;

    public PatientWorkflowService(
        IPatientRepository repository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<PatientWorkflowService> logger)
    {
        _logger = logger;
        _repository = repository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<PatientDto> ChangeStatusAsync(
        Guid id,
        ChangePatientStatusRequest? request,
        CancellationToken cancellationToken)
    {
        ChangePatientStatusRequestValidator.EnsureValid(request);
        var valid = request!;

        var patient = await LoadForUpdateAsync(id, cancellationToken);

        var previousStatus = patient.Status;
        patient.ChangeStatus(valid.TargetStatus!.Value, valid.DispositionNote, _timeProvider);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Never the disposition note. Failures (409 conflicts included) are logged once by GlobalExceptionHandler.
        _logger.LogInformation(
            "Patient status changed. PatientId {PatientId}, From {PreviousStatus}, To {Status}",
            patient.Id,
            previousStatus,
            patient.Status);

        return PatientMapper.ToDto(patient);
    }

    public async Task<PatientDto> OverridePriorityAsync(
        Guid id,
        OverridePriorityRequest? request,
        CancellationToken cancellationToken)
    {
        OverridePriorityRequestValidator.EnsureValid(request);
        var valid = request!;

        var patient = await LoadForUpdateAsync(id, cancellationToken);

        patient.ApplyPriorityOverride(valid.Priority!.Value, valid.Reason, valid.OverriddenBy, _timeProvider);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Never the reason or the nurse name.
        _logger.LogInformation(
            "Patient priority overridden. PatientId {PatientId}, CalculatedPriority {CalculatedPriority}, CurrentPriority {CurrentPriority}",
            patient.Id,
            patient.CalculatedPriority,
            patient.CurrentPriority);

        return PatientMapper.ToDto(patient);
    }

    private async Task<Patient> LoadForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        await _repository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException(MessageCatalog.Get("PatientNotFound"));
}
