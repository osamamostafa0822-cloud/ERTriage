using ERTriage.Application.Patients.Dtos;
using ERTriage.Domain.Patients;

namespace ERTriage.Application.Patients;

/// <summary>Explicit manual mapping from the aggregate to its public DTO (no mapping library, D-14).</summary>
public static class PatientMapper
{
    public static PatientDto ToDto(Patient patient)
    {
        ArgumentNullException.ThrowIfNull(patient);

        return new PatientDto(
            patient.Id,
            patient.FullName,
            patient.Age,
            patient.ChiefComplaint,
            ToDto(patient.Vitals),
            patient.ArrivalTime,
            patient.CalculatedPriority,
            patient.PriorityReason,
            patient.OverridePriority,
            patient.OverrideReason,
            patient.OverriddenBy,
            patient.OverriddenAt,
            patient.CurrentPriority,
            patient.IsOverridden,
            patient.Status,
            patient.TreatmentStartedAt,
            patient.CompletedAt,
            patient.DispositionNote,
            patient.AllowedTransitions());
    }

    public static VitalSignsDto ToDto(VitalSigns vitals)
    {
        ArgumentNullException.ThrowIfNull(vitals);

        return new VitalSignsDto(vitals.HeartRate, vitals.SystolicBp, vitals.DiastolicBp, vitals.TemperatureC, vitals.SpO2);
    }
}
