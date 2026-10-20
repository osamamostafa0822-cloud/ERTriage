using ERTriage.Domain.Patients;

namespace ERTriage.Application.Tests;

/// <summary>Hand-written fake clock (D-07): no extra test package.</summary>
internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

    public static FakeTimeProvider Clock() => new(Now);

    /// <summary>Registers a valid fictional patient through the Domain; arrival is <paramref name="minutesWaited"/> before now.</summary>
    public static Patient NewPatient(FakeTimeProvider clock, int minutesWaited = 0, string name = "Test Patient Alpha") =>
        Domain.Patients.Patient.Register(
            name,
            54,
            "Shortness of breath",
            VitalSigns.Create(112, 138, 86, 37.9m, 88),
            clock.GetUtcNow().AddMinutes(-minutesWaited),
            clock);
}
