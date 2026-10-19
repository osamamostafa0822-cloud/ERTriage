using ERTriage.Domain.Common;
using ERTriage.Domain.Patients;

namespace ERTriage.Domain.Tests;

public class PatientPriorityOverrideTests
{
    private const string Reason = "Patient appears acutely distressed; escalating.";

    [Fact]
    public void ApplyPriorityOverride_Valid_ChangesCurrentButKeepsCalculated()
    {
        var clock = TestData.Clock();
        var patient = TestData.Register(clock, TestData.Vitals(spO2: 98));
        clock.Advance(TimeSpan.FromMinutes(7));

        patient.ApplyPriorityOverride(1, Reason, "Nurse A", clock);

        Assert.Equal(4, patient.CalculatedPriority);
        Assert.Equal("All scored vitals within normal bands", patient.PriorityReason);
        Assert.Equal(1, patient.OverridePriority);
        Assert.Equal(1, patient.CurrentPriority);
        Assert.Equal(Reason, patient.OverrideReason);
        Assert.Equal("Nurse A", patient.OverriddenBy);
        Assert.Equal(TestData.Now.AddMinutes(7).UtcDateTime, patient.OverriddenAt);
        Assert.True(patient.IsOverridden);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    public void ApplyPriorityOverride_MissingOrTooShortReason_ThrowsAndChangesNothing(string? reason)
    {
        var clock = TestData.Clock();
        var patient = TestData.Register(clock);

        Assert.Throws<DomainValidationException>(() => patient.ApplyPriorityOverride(1, reason, null, clock));

        Assert.Null(patient.OverridePriority);
        Assert.Null(patient.OverrideReason);
        Assert.Null(patient.OverriddenAt);
        Assert.Equal(patient.CalculatedPriority, patient.CurrentPriority);
    }

    [Fact]
    public void ApplyPriorityOverride_MissingReason_MessageSaysReasonIsRequired()
    {
        var patient = TestData.Register(TestData.Clock());

        var exception = Assert.Throws<DomainValidationException>(
            () => patient.ApplyPriorityOverride(1, null, null, TestData.Clock()));

        Assert.Contains("required", exception.Message);
    }

    [Fact]
    public void ApplyPriorityOverride_ReasonLengthLimits_AreEnforced()
    {
        var clock = TestData.Clock();
        var patient = TestData.Register(clock);

        patient.ApplyPriorityOverride(2, new string('r', PatientRules.OverrideReasonMaxLength), null, clock);
        Assert.Throws<DomainValidationException>(
            () => patient.ApplyPriorityOverride(2, new string('r', PatientRules.OverrideReasonMaxLength + 1), null, clock));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void ApplyPriorityOverride_PriorityOutOfRange_Throws(int priority)
    {
        var clock = TestData.Clock();
        var patient = TestData.Register(clock);

        Assert.Throws<DomainValidationException>(() => patient.ApplyPriorityOverride(priority, Reason, null, clock));
        Assert.Null(patient.OverridePriority);
    }

    [Fact]
    public void ApplyPriorityOverride_OverriddenByTooLong_Throws()
    {
        var clock = TestData.Clock();
        var patient = TestData.Register(clock);

        Assert.Throws<DomainValidationException>(
            () => patient.ApplyPriorityOverride(1, Reason, new string('n', PatientRules.OverriddenByMaxLength + 1), clock));
    }

    [Fact]
    public void ApplyPriorityOverride_BlankOverriddenBy_IsStoredAsNull()
    {
        var clock = TestData.Clock();
        var patient = TestData.Register(clock);

        patient.ApplyPriorityOverride(1, Reason, "   ", clock);

        Assert.Null(patient.OverriddenBy);
    }

    [Fact]
    public void ApplyPriorityOverride_SameAsCalculated_StillRequiresReasonAndIsRecorded()
    {
        var clock = TestData.Clock();
        var patient = TestData.Register(clock, TestData.Vitals(spO2: 91)); // calculated 2

        Assert.Throws<DomainValidationException>(() => patient.ApplyPriorityOverride(2, "", null, clock));

        patient.ApplyPriorityOverride(2, Reason, null, clock);
        Assert.True(patient.IsOverridden);
        Assert.Equal(2, patient.CurrentPriority);
    }

    [Fact]
    public void ApplyPriorityOverride_Twice_SecondReplacesFirst()
    {
        var clock = TestData.Clock();
        var patient = TestData.Register(clock);
        patient.ApplyPriorityOverride(1, "First reason", "Nurse A", clock);
        clock.Advance(TimeSpan.FromMinutes(3));

        patient.ApplyPriorityOverride(3, "Second reason", null, clock);

        Assert.Equal(3, patient.OverridePriority);
        Assert.Equal(3, patient.CurrentPriority);
        Assert.Equal("Second reason", patient.OverrideReason);
        Assert.Null(patient.OverriddenBy);
        Assert.Equal(TestData.Now.AddMinutes(3).UtcDateTime, patient.OverriddenAt);
    }

    [Theory]
    [InlineData(PatientStatus.Waiting)]
    [InlineData(PatientStatus.InTreatment)]
    public void ApplyPriorityOverride_WaitingOrInTreatment_Allowed(PatientStatus status)
    {
        var clock = TestData.Clock();
        var patient = TestData.InStatus(status, clock);

        patient.ApplyPriorityOverride(1, Reason, null, clock);

        Assert.Equal(1, patient.CurrentPriority);
    }

    [Theory]
    [InlineData(PatientStatus.Discharged)]
    [InlineData(PatientStatus.Admitted)]
    public void ApplyPriorityOverride_AfterDisposition_ThrowsStatusConflict(PatientStatus status)
    {
        var clock = TestData.Clock();
        var patient = TestData.InStatus(status, clock);

        var exception = Assert.Throws<PatientStatusConflictException>(
            () => patient.ApplyPriorityOverride(1, Reason, null, clock));

        Assert.Contains(status.ToString(), exception.Message);
        Assert.Null(patient.OverridePriority);
    }

    // Condition 2: CurrentPriority always equals OverridePriority ?? CalculatedPriority.
    [Theory]
    [InlineData(88)]
    [InlineData(91)]
    [InlineData(95)]
    [InlineData(98)]
    public void CurrentPriority_AllCalculatedAndOverrideCombinations_EqualsOverrideOrCalculated(int spO2)
    {
        foreach (var overridePriority in new[] { 1, 2, 3, 4 })
        {
            var clock = TestData.Clock();
            var patient = TestData.Register(clock, TestData.Vitals(spO2: spO2));
            var calculated = patient.CalculatedPriority;

            Assert.Equal(patient.OverridePriority ?? patient.CalculatedPriority, patient.CurrentPriority);

            patient.ApplyPriorityOverride(overridePriority, Reason, null, clock);

            Assert.Equal(calculated, patient.CalculatedPriority);
            Assert.Equal(overridePriority, patient.OverridePriority);
            Assert.Equal(patient.OverridePriority ?? patient.CalculatedPriority, patient.CurrentPriority);
        }
    }

    [Theory]
    [InlineData(nameof(Patient.CalculatedPriority))]
    [InlineData(nameof(Patient.OverridePriority))]
    [InlineData(nameof(Patient.CurrentPriority))]
    public void PriorityProperties_Setters_ArePrivate(string propertyName)
    {
        var setter = typeof(Patient).GetProperty(propertyName)!.SetMethod!;

        Assert.True(setter.IsPrivate);
    }
}
