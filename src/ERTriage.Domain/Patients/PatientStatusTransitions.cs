using System.Collections.ObjectModel;

namespace ERTriage.Domain.Patients;

/// <summary>
/// The single source of truth for allowed status changes (docs/03-features/patient-status-workflow.md).
/// Waiting -> Discharged is rejected (challenge PDF). The rest of the matrix is our design:
/// Waiting -> InTreatment; InTreatment -> Discharged | Admitted; Discharged and Admitted are terminal.
/// </summary>
public static class PatientStatusTransitions
{
    private static readonly ReadOnlyCollection<PatientStatus> FromWaiting =
        Array.AsReadOnly(new[] { PatientStatus.InTreatment });

    private static readonly ReadOnlyCollection<PatientStatus> FromInTreatment =
        Array.AsReadOnly(new[] { PatientStatus.Discharged, PatientStatus.Admitted });

    private static readonly ReadOnlyCollection<PatientStatus> Terminal =
        Array.AsReadOnly(Array.Empty<PatientStatus>());

    public static IReadOnlyList<PatientStatus> From(PatientStatus current) =>
        current switch
        {
            PatientStatus.Waiting => FromWaiting,
            PatientStatus.InTreatment => FromInTreatment,
            _ => Terminal
        };

    public static bool IsAllowed(PatientStatus from, PatientStatus to) => From(from).Contains(to);
}
