using ERTriage.Domain.Patients;
using ERTriage.Application.Triage.Waiting;

namespace ERTriage.Application.Tests;

public class WaitingTimeEvaluatorTests
{
    [Theory]
    [InlineData(0, 0, WaitingLevel.Normal)]
    [InlineData(1, 1, WaitingLevel.Normal)]
    [InlineData(29, 29, WaitingLevel.Normal)]
    [InlineData(30, 30, WaitingLevel.Warning)]
    [InlineData(59, 59, WaitingLevel.Warning)]
    [InlineData(60, 60, WaitingLevel.Critical)]
    [InlineData(61, 61, WaitingLevel.Critical)]
    [InlineData(600, 600, WaitingLevel.Critical)]
    public void Evaluate_WaitingPatient_UsesFixedThresholds(int minutesWaited, int expectedMinutes, WaitingLevel expectedLevel)
    {
        var clock = TestData.Clock();
        var patient = TestData.NewPatient(clock, minutesWaited);

        var result = Evaluate(patient, clock.GetUtcNow());

        Assert.Equal(expectedMinutes, result.Minutes);
        Assert.Equal(expectedLevel, result.Level);
    }

    [Fact]
    public void Evaluate_PartialMinute_IsFlooredNotRounded()
    {
        var clock = TestData.Clock();
        var patient = TestData.NewPatient(clock, 29);

        var result = Evaluate(patient, clock.GetUtcNow().AddSeconds(59));

        Assert.Equal(29, result.Minutes);
        Assert.Equal(WaitingLevel.Normal, result.Level);
    }

    [Fact]
    public void Evaluate_ArrivalSlightlyInTheFuture_CountsAsZeroNotNegative()
    {
        var clock = TestData.Clock();
        var patient = TestData.NewPatient(clock, minutesWaited: -4);

        var result = Evaluate(patient, clock.GetUtcNow());

        Assert.Equal(0, result.Minutes);
        Assert.Equal(WaitingLevel.Normal, result.Level);
    }

    [Fact]
    public void Evaluate_TimeAdvances_LevelRisesWithTheClock()
    {
        var clock = TestData.Clock();
        var patient = TestData.NewPatient(clock, 0);

        clock.Advance(TimeSpan.FromMinutes(45));

        var result = Evaluate(patient, clock.GetUtcNow());

        Assert.Equal(45, result.Minutes);
        Assert.Equal(WaitingLevel.Warning, result.Level);
    }

    [Fact]
    public void Evaluate_InTreatment_IsFrozenAtTreatmentStartWithNoLevel()
    {
        var clock = TestData.Clock();
        var patient = TestData.NewPatient(clock, 90);
        patient.ChangeStatus(PatientStatus.InTreatment, null, clock); // treatment starts at "now": waited 90 minutes

        var result = Evaluate(patient, clock.GetUtcNow().AddHours(5));

        Assert.Equal(90, result.Minutes);
        Assert.Equal(WaitingLevel.None, result.Level);
    }

    [Theory]
    [InlineData(PatientStatus.Discharged)]
    [InlineData(PatientStatus.Admitted)]
    public void Evaluate_CompletedPatient_HasNoIndicator(PatientStatus final)
    {
        var clock = TestData.Clock();
        var patient = TestData.NewPatient(clock, 120);
        patient.ChangeStatus(PatientStatus.InTreatment, null, clock);
        patient.ChangeStatus(final, "Done.", clock);

        var result = Evaluate(patient, clock.GetUtcNow());

        Assert.Null(result.Minutes);
        Assert.Equal(WaitingLevel.None, result.Level);
    }

    [Fact]
    public void Thresholds_AreThirtyAndSixtyMinutes()
    {
        Assert.Equal(30, WaitingTimeEvaluator.WarningMinutes);
        Assert.Equal(60, WaitingTimeEvaluator.CriticalMinutes);
    }

    // The evaluator takes the three fields it needs (the board passes its projected row); tests build them from a real patient.
    private static WaitingTime Evaluate(Patient patient, DateTimeOffset now) =>
        WaitingTimeEvaluator.Evaluate(patient.Status, patient.ArrivalTime, patient.TreatmentStartedAt, now);
}
