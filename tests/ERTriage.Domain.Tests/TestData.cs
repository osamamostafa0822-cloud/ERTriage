using ERTriage.Domain.Patients;

namespace ERTriage.Domain.Tests;

/// <summary>Hand-written fake (D-07): no extra test package needed.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    public const string Note = "Treated and released with follow-up advice.";

    public static FakeTimeProvider Clock() => new(Now);

    /// <summary>Normal baseline vitals (priority 4); override one reading to probe a threshold.</summary>
    public static VitalSigns Vitals(
        int heartRate = 80,
        int systolicBp = 120,
        decimal temperatureC = 37.0m,
        int spO2 = 98,
        int? diastolicBp = null) =>
        VitalSigns.Create(heartRate, systolicBp, diastolicBp ?? Math.Min(80, systolicBp - 10), temperatureC, spO2);

    public static Patient Register(FakeTimeProvider clock, VitalSigns? vitals = null, DateTimeOffset? arrival = null) =>
        Patient.Register("Test Patient Alpha", 54, "Shortness of breath", vitals ?? Vitals(), arrival ?? clock.GetUtcNow(), clock);

    public static Patient InStatus(PatientStatus status, FakeTimeProvider clock, VitalSigns? vitals = null)
    {
        var patient = Register(clock, vitals);

        switch (status)
        {
            case PatientStatus.InTreatment:
                patient.ChangeStatus(PatientStatus.InTreatment, null, clock);
                break;
            case PatientStatus.Discharged:
            case PatientStatus.Admitted:
                patient.ChangeStatus(PatientStatus.InTreatment, null, clock);
                patient.ChangeStatus(status, Note, clock);
                break;
        }

        return patient;
    }
}
