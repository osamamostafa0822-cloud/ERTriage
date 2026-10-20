using ERTriage.Domain.Common;
using ERTriage.Application.Common;
using ERTriage.Application.Patients.Dtos;
using ERTriage.Application.Patients.Requests;
using ERTriage.Application.Patients.Validation;
using ERTriage.Domain.Patients;
using Microsoft.Extensions.Logging;

namespace ERTriage.Application.Patients;

/// <summary>
/// Use cases for registration: validate (all field errors at once), register through the Domain, save once, map to a DTO.
/// Business invariants stay in <see cref="Patient"/>; the clock comes from the injected <see cref="TimeProvider"/> (D-07).
/// </summary>
public sealed class PatientRegistrationService : IPatientRegistrationService
{
    private readonly IPatientRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PatientRegistrationService> _logger;

    public PatientRegistrationService(
        IPatientRepository repository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<PatientRegistrationService> logger)
    {
        _logger = logger;
        _repository = repository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<PatientDto> RegisterAsync(RegisterPatientRequest? request, CancellationToken cancellationToken)
    {
        RegisterPatientRequestValidator.EnsureValid(request, _timeProvider.GetUtcNow());

        // EnsureValid guarantees the request and every required value are present.
        var valid = request!;
        var vitals = VitalSigns.Create(
            valid.HeartRate!.Value,
            valid.SystolicBp!.Value,
            valid.DiastolicBp!.Value,
            valid.TemperatureC!.Value,
            valid.SpO2!.Value);

        var patient = Patient.Register(
            valid.FullName!,
            valid.Age!.Value,
            valid.ChiefComplaint!,
            vitals,
            valid.ArrivalTime!.Value,
            _timeProvider);

        _repository.Add(patient);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Identifiers and priority only: never the name, complaint or vitals. Failures are logged once by GlobalExceptionHandler.
        _logger.LogInformation(
            "Patient registered. PatientId {PatientId}, CalculatedPriority {CalculatedPriority}",
            patient.Id,
            patient.CalculatedPriority);

        return PatientMapper.ToDto(patient);
    }

    public async Task<PatientDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var patient = await _repository.GetByIdReadOnlyAsync(id, cancellationToken)
            ?? throw new NotFoundException(MessageCatalog.Get("PatientNotFound"));

        return PatientMapper.ToDto(patient);
    }
}
