using ERTriage.Domain.Common;
using ERTriage.Domain.Patients;

namespace ERTriage.Domain.Tests;

public class PatientStatusTransitionsTests
{
    // All 16 cells of the transition matrix (docs/03-features/patient-status-workflow.md).
    [Theory]
    [InlineData(PatientStatus.Waiting, PatientStatus.Waiting, false)]
    [InlineData(PatientStatus.Waiting, PatientStatus.InTreatment, true)]
    [InlineData(PatientStatus.Waiting, PatientStatus.Discharged, false)] // challenge PDF
    [InlineData(PatientStatus.Waiting, PatientStatus.Admitted, false)]
    [InlineData(PatientStatus.InTreatment, PatientStatus.Waiting, false)]
    [InlineData(PatientStatus.InTreatment, PatientStatus.InTreatment, false)]
    [InlineData(PatientStatus.InTreatment, PatientStatus.Discharged, true)]
    [InlineData(PatientStatus.InTreatment, PatientStatus.Admitted, true)]
    [InlineData(PatientStatus.Discharged, PatientStatus.Waiting, false)]
    [InlineData(PatientStatus.Discharged, PatientStatus.InTreatment, false)]
    [InlineData(PatientStatus.Discharged, PatientStatus.Discharged, false)]
    [InlineData(PatientStatus.Discharged, PatientStatus.Admitted, false)]
    [InlineData(PatientStatus.Admitted, PatientStatus.Waiting, false)]
    [InlineData(PatientStatus.Admitted, PatientStatus.InTreatment, false)]
    [InlineData(PatientStatus.Admitted, PatientStatus.Discharged, false)]
    [InlineData(PatientStatus.Admitted, PatientStatus.Admitted, false)]
    public void Matrix_EveryCell_MatchesDocumentedRule(PatientStatus from, PatientStatus to, bool allowed)
    {
        Assert.Equal(allowed, PatientStatusTransitions.IsAllowed(from, to));
    }

    [Theory]
    [InlineData(PatientStatus.Waiting, PatientStatus.Waiting, false)]
    [InlineData(PatientStatus.Waiting, PatientStatus.InTreatment, true)]
    [InlineData(PatientStatus.Waiting, PatientStatus.Discharged, false)]
    [InlineData(PatientStatus.Waiting, PatientStatus.Admitted, false)]
    [InlineData(PatientStatus.InTreatment, PatientStatus.Waiting, false)]
    [InlineData(PatientStatus.InTreatment, PatientStatus.InTreatment, false)]
    [InlineData(PatientStatus.InTreatment, PatientStatus.Discharged, true)]
    [InlineData(PatientStatus.InTreatment, PatientStatus.Admitted, true)]
    [InlineData(PatientStatus.Discharged, PatientStatus.Waiting, false)]
    [InlineData(PatientStatus.Discharged, PatientStatus.InTreatment, false)]
    [InlineData(PatientStatus.Discharged, PatientStatus.Discharged, false)]
    [InlineData(PatientStatus.Discharged, PatientStatus.Admitted, false)]
    [InlineData(PatientStatus.Admitted, PatientStatus.Waiting, false)]
    [InlineData(PatientStatus.Admitted, PatientStatus.InTreatment, false)]
    [InlineData(PatientStatus.Admitted, PatientStatus.Discharged, false)]
    [InlineData(PatientStatus.Admitted, PatientStatus.Admitted, false)]
    public void Patient_ChangeStatus_EveryCell_AppliesOrRejects(PatientStatus from, PatientStatus to, bool allowed)
    {
        var clock = TestData.Clock();
        var patient = TestData.InStatus(from, clock);
        var note = to is PatientStatus.Discharged or PatientStatus.Admitted ? TestData.Note : null;

        if (allowed)
        {
            patient.ChangeStatus(to, note, clock);
            Assert.Equal(to, patient.Status);
        }
        else
        {
            var exception = Assert.Throws<InvalidStatusTransitionException>(() => patient.ChangeStatus(to, note, clock));
            Assert.Equal(from.ToString(), exception.From);
            Assert.Equal(to.ToString(), exception.To);
            Assert.Equal(from, patient.Status);
        }
    }

    [Theory]
    [InlineData(PatientStatus.Waiting, 1)]
    [InlineData(PatientStatus.InTreatment, 2)]
    [InlineData(PatientStatus.Discharged, 0)]
    [InlineData(PatientStatus.Admitted, 0)]
    public void AllowedTransitions_PerStatus_HasExpectedCount(PatientStatus status, int expectedCount)
    {
        Assert.Equal(expectedCount, PatientStatusTransitions.From(status).Count);
        Assert.Equal(expectedCount, TestData.InStatus(status, TestData.Clock()).AllowedTransitions().Count);
    }
}
