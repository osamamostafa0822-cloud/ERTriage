using ERTriage.Application.Patients.Dtos;
using ERTriage.Application.Patients.Requests;

namespace ERTriage.Application.Patients;

/// <summary>Registers arriving patients and reads one patient back (docs/03-features/patient-registration.md).</summary>
public interface IPatientRegistrationService
{
    /// <summary>Validates, registers through the Domain (which calculates the priority) and saves once. Throws <c>ValidationException</c> (400).</summary>
    Task<PatientDto> RegisterAsync(RegisterPatientRequest? request, CancellationToken cancellationToken);

    /// <summary>Throws <c>NotFoundException</c> (404) when the patient does not exist.</summary>
    Task<PatientDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
