using ERTriage.Domain.Common;

namespace ERTriage.Domain.Patients;

public enum PatientStatus
{
    [MessageKey("Waiting")] Waiting,
    [MessageKey("InTreatment")] InTreatment,
    [MessageKey("Discharged")] Discharged,
    [MessageKey("Admitted")] Admitted
}
