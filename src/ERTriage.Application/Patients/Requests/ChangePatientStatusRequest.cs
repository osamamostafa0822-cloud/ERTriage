using ERTriage.Domain.Patients;

namespace ERTriage.Application.Patients.Requests;

/// <summary>Body of <c>POST /api/patients/{id}/status</c>. The note is required for Discharged/Admitted only.</summary>
public sealed record ChangePatientStatusRequest(PatientStatus? TargetStatus, string? DispositionNote);
