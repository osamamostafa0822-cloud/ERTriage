using ERTriage.Domain.Common;
using ERTriage.Domain.Patients;
using ERTriage.Domain.Triage;

namespace ERTriage.Domain.Tests;

public class PatientRegistrationTests
{
    [Fact]
    public void Register_ValidInput_CreatesWaitingPatientWithCalculatedPriority()
    {
        var clock = TestData.Clock();

        var patient = Patient.Register(
            "Test Patient Alpha", 54, "Shortness of breath", TestData.Vitals(spO2: 88), clock.GetUtcNow(), clock);

        Assert.Equal(PatientStatus.Waiting, patient.Status);
        Assert.Equal(1, patient.CalculatedPriority);
        Assert.Equal("SpO2 88% < 90", patient.PriorityReason);
        Assert.Equal(1, patient.CurrentPriority);
        Assert.Null(patient.OverridePriority);
        Assert.False(patient.IsOverridden);
        Assert.Equal(TestData.Now.UtcDateTime, patient.CreatedAt);
        Assert.Null(patient.TreatmentStartedAt);
        Assert.Null(patient.CompletedAt);
        Assert.Null(patient.DispositionNote);
    }

    [Fact]
    public void Register_Text_IsTrimmed()
    {
        var clock = TestData.Clock();

        var patient = Patient.Register("  Test Patient Alpha  ", 54, "  Chest pain ", TestData.Vitals(), clock.GetUtcNow(), clock);

        Assert.Equal("Test Patient Alpha", patient.FullName);
        Assert.Equal("Chest pain", patient.ChiefComplaint);
    }

    [Fact]
    public void Register_Id_IsGuidVersion7WithTimeProviderTimestamp()
    {
        var clock = TestData.Clock();

        var patient = TestData.Register(clock);

        Assert.Equal(7, patient.Id.Version);

        var bytes = patient.Id.ToByteArray(bigEndian: true);
        long milliseconds = 0;
        for (var i = 0; i < 6; i++)
        {
            milliseconds = (milliseconds << 8) | bytes[i];
        }

        Assert.Equal(TestData.Now.ToUnixTimeMilliseconds(), milliseconds);
    }

    [Fact]
    public void Register_ArrivalWithOffset_IsStoredAsUtc()
    {
        var clock = TestData.Clock();
        var arrival = new DateTimeOffset(2026, 10, 19, 13, 30, 0, TimeSpan.FromHours(2));

        var patient = TestData.Register(clock, arrival: arrival);

        Assert.Equal(new DateTime(2026, 10, 19, 11, 30, 0, DateTimeKind.Utc), patient.ArrivalTime);
        Assert.Equal(DateTimeKind.Utc, patient.ArrivalTime.Kind);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void Register_ArrivalWithinFiveMinutesAhead_Accepted(int minutesAhead)
    {
        var clock = TestData.Clock();

        TestData.Register(clock, arrival: TestData.Now.AddMinutes(minutesAhead));
    }

    [Fact]
    public void Register_ArrivalSixMinutesAhead_Throws()
    {
        var clock = TestData.Clock();

        Assert.Throws<DomainValidationException>(() => TestData.Register(clock, arrival: TestData.Now.AddMinutes(6)));
    }

    [Fact]
    public void Register_ArrivalInThePast_Accepted()
    {
        var clock = TestData.Clock();

        TestData.Register(clock, arrival: TestData.Now.AddHours(-3));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    public void Register_InvalidFullName_Throws(string? fullName)
    {
        var clock = TestData.Clock();

        Assert.Throws<DomainValidationException>(
            () => Patient.Register(fullName, 54, "Chest pain", TestData.Vitals(), clock.GetUtcNow(), clock));
    }

    [Fact]
    public void Register_FullNameLengthLimits_AreEnforced()
    {
        var clock = TestData.Clock();

        Patient.Register(new string('A', PatientRules.FullNameMaxLength), 54, "Chest pain", TestData.Vitals(), clock.GetUtcNow(), clock);
        Assert.Throws<DomainValidationException>(
            () => Patient.Register(new string('A', PatientRules.FullNameMaxLength + 1), 54, "Chest pain", TestData.Vitals(), clock.GetUtcNow(), clock));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(121)]
    public void Register_AgeOutOfRange_Throws(int age)
    {
        var clock = TestData.Clock();

        Assert.Throws<DomainValidationException>(
            () => Patient.Register("Test Patient Alpha", age, "Chest pain", TestData.Vitals(), clock.GetUtcNow(), clock));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(120)]
    public void Register_AgeBounds_Accepted(int age)
    {
        var clock = TestData.Clock();

        Patient.Register("Test Patient Alpha", age, "Chest pain", TestData.Vitals(), clock.GetUtcNow(), clock);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("x")]
    public void Register_InvalidChiefComplaint_Throws(string? complaint)
    {
        var clock = TestData.Clock();

        Assert.Throws<DomainValidationException>(
            () => Patient.Register("Test Patient Alpha", 54, complaint, TestData.Vitals(), clock.GetUtcNow(), clock));
    }

    [Fact]
    public void Register_ChiefComplaintTooLong_Throws()
    {
        var clock = TestData.Clock();

        Assert.Throws<DomainValidationException>(
            () => Patient.Register(
                "Test Patient Alpha", 54, new string('c', PatientRules.ChiefComplaintMaxLength + 1), TestData.Vitals(), clock.GetUtcNow(), clock));
    }

    [Fact]
    public void Register_MissingVitals_Throws()
    {
        var clock = TestData.Clock();

        Assert.Throws<DomainValidationException>(
            () => Patient.Register("Test Patient Alpha", 54, "Chest pain", null, clock.GetUtcNow(), clock));
    }

    [Fact]
    public void Register_NullTimeProvider_Throws() =>
        Assert.Throws<ArgumentNullException>(
            () => Patient.Register("Test Patient Alpha", 54, "Chest pain", TestData.Vitals(), TestData.Now, null!));

    [Theory]
    [InlineData(88, 1)]
    [InlineData(91, 2)]
    [InlineData(95, 3)]
    [InlineData(98, 4)]
    public void Register_CurrentPriority_EqualsCalculatedPriority(int spO2, int expected)
    {
        var patient = TestData.Register(TestData.Clock(), TestData.Vitals(spO2: spO2));

        Assert.Equal(expected, patient.CalculatedPriority);
        Assert.Equal(expected, patient.CurrentPriority);
        Assert.True(TriagePriority.IsValid(patient.CurrentPriority));
    }
}
