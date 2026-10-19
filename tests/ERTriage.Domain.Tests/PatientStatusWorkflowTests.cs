using ERTriage.Domain.Common;
using ERTriage.Domain.Patients;

namespace ERTriage.Domain.Tests;

public class PatientStatusWorkflowTests
{
    [Fact]
    public void ChangeStatus_WaitingToInTreatment_SetsTreatmentStartedAt()
    {
        var clock = TestData.Clock();
        var patient = TestData.Register(clock);
        clock.Advance(TimeSpan.FromMinutes(20));

        patient.ChangeStatus(PatientStatus.InTreatment, null, clock);

        Assert.Equal(PatientStatus.InTreatment, patient.Status);
        Assert.Equal(TestData.Now.AddMinutes(20).UtcDateTime, patient.TreatmentStartedAt);
        Assert.Null(patient.CompletedAt);
    }

    [Theory]
    [InlineData(PatientStatus.Discharged)]
    [InlineData(PatientStatus.Admitted)]
    public void ChangeStatus_InTreatmentToDisposition_SetsCompletedAtAndTrimmedNote(PatientStatus target)
    {
        var clock = TestData.Clock();
        var patient = TestData.InStatus(PatientStatus.InTreatment, clock);
        clock.Advance(TimeSpan.FromMinutes(45));

        patient.ChangeStatus(target, "  " + TestData.Note + "  ", clock);

        Assert.Equal(target, patient.Status);
        Assert.Equal(TestData.Note, patient.DispositionNote);
        Assert.Equal(TestData.Now.AddMinutes(45).UtcDateTime, patient.CompletedAt);
    }

    // The explicit example from the challenge PDF.
    [Fact]
    public void ChangeStatus_WaitingToDischarged_IsRejected()
    {
        var clock = TestData.Clock();
        var patient = TestData.Register(clock);

        var exception = Assert.Throws<InvalidStatusTransitionException>(
            () => patient.ChangeStatus(PatientStatus.Discharged, TestData.Note, clock));

        Assert.Equal("Cannot change status from Waiting to Discharged.", exception.Message);
        Assert.Equal(PatientStatus.Waiting, patient.Status);
        Assert.Null(patient.CompletedAt);
        Assert.Null(patient.DispositionNote);
    }

    [Fact]
    public void ChangeStatus_InvalidTransitionWithoutNote_ReportsTheTransitionFirst()
    {
        var patient = TestData.Register(TestData.Clock());

        Assert.Throws<InvalidStatusTransitionException>(
            () => patient.ChangeStatus(PatientStatus.Admitted, null, TestData.Clock()));
    }

    [Theory]
    [InlineData(PatientStatus.Discharged, null)]
    [InlineData(PatientStatus.Discharged, "")]
    [InlineData(PatientStatus.Discharged, "   ")]
    [InlineData(PatientStatus.Admitted, null)]
    [InlineData(PatientStatus.Admitted, "")]
    public void ChangeStatus_DispositionWithoutNote_ThrowsAndChangesNothing(PatientStatus target, string? note)
    {
        var clock = TestData.Clock();
        var patient = TestData.InStatus(PatientStatus.InTreatment, clock);

        Assert.Throws<DomainValidationException>(() => patient.ChangeStatus(target, note, clock));

        Assert.Equal(PatientStatus.InTreatment, patient.Status);
        Assert.Null(patient.CompletedAt);
        Assert.Null(patient.DispositionNote);
    }

    [Fact]
    public void ChangeStatus_NoteLengthLimits_AreEnforced()
    {
        var clock = TestData.Clock();
        var patient = TestData.InStatus(PatientStatus.InTreatment, clock);

        Assert.Throws<DomainValidationException>(
            () => patient.ChangeStatus(PatientStatus.Discharged, new string('n', PatientRules.DispositionNoteMaxLength + 1), clock));

        patient.ChangeStatus(PatientStatus.Discharged, new string('n', PatientRules.DispositionNoteMaxLength), clock);
        Assert.Equal(PatientStatus.Discharged, patient.Status);
    }

    [Fact]
    public void ChangeStatus_NoteWhenStartingTreatment_ThrowsAndChangesNothing()
    {
        var clock = TestData.Clock();
        var patient = TestData.Register(clock);

        Assert.Throws<DomainValidationException>(
            () => patient.ChangeStatus(PatientStatus.InTreatment, "Should not be here", clock));

        Assert.Equal(PatientStatus.Waiting, patient.Status);
        Assert.Null(patient.TreatmentStartedAt);
    }

    [Theory]
    [InlineData(PatientStatus.Discharged)]
    [InlineData(PatientStatus.Admitted)]
    public void ChangeStatus_FromTerminalStatus_AlwaysRejected(PatientStatus terminal)
    {
        var clock = TestData.Clock();
        var patient = TestData.InStatus(terminal, clock);

        foreach (var target in Enum.GetValues<PatientStatus>())
        {
            Assert.Throws<InvalidStatusTransitionException>(() => patient.ChangeStatus(target, TestData.Note, clock));
        }

        Assert.Equal(terminal, patient.Status);
    }

    [Fact]
    public void ChangeStatus_UndefinedTargetValue_IsRejected()
    {
        var clock = TestData.Clock();
        var patient = TestData.Register(clock);

        Assert.Throws<InvalidStatusTransitionException>(() => patient.ChangeStatus((PatientStatus)99, null, clock));
    }

    [Fact]
    public void ChangeStatus_DoesNotAffectPriorities()
    {
        var clock = TestData.Clock();
        var patient = TestData.Register(clock, TestData.Vitals(spO2: 91));
        patient.ApplyPriorityOverride(1, "Escalated by nurse", null, clock);

        patient.ChangeStatus(PatientStatus.InTreatment, null, clock);

        Assert.Equal(2, patient.CalculatedPriority);
        Assert.Equal(1, patient.CurrentPriority);
    }
}
