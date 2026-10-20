using ERTriage.Domain.Patients;

namespace ERTriage.Infrastructure.Persistence.Seed;

/// <summary>
/// Turns a validated seed record into a <see cref="Patient"/> using <b>only Domain methods</b>:
/// <see cref="Patient.Register"/>, then <see cref="Patient.ApplyPriorityOverride"/> and <see cref="Patient.ChangeStatus"/>
/// in chronological order. Priority, reason, current priority, ids and timestamps are all produced by the Domain.
/// </summary>
internal static class FictionalPatientFactory
{
    public static Patient Create(ValidatedFictionalPatient seed, DateTimeOffset now)
    {
        var arrival = now.AddMinutes(-seed.ArrivedMinutesAgo);
        var clock = new SteppingClock(arrival);

        var patient = Patient.Register(
            seed.FullName,
            seed.Age,
            seed.ChiefComplaint,
            VitalSigns.Create(seed.HeartRate, seed.SystolicBp, seed.DiastolicBp, seed.TemperatureC, seed.SpO2),
            arrival,
            clock);

        foreach (var step in Steps(seed).OrderBy(s => s.MinutesAfterArrival).ThenBy(s => s.Order))
        {
            clock.MoveTo(arrival.AddMinutes(step.MinutesAfterArrival));
            step.Apply(patient, clock);
        }

        return patient;
    }

    private static IEnumerable<Step> Steps(ValidatedFictionalPatient seed)
    {
        // Order breaks ties at the same minute: an override is applied before a status change.
        if (seed.PriorityOverride is { } priorityOverride)
        {
            yield return new Step(priorityOverride.MinutesAfterArrival, 0, (patient, clock) =>
                patient.ApplyPriorityOverride(priorityOverride.Priority, priorityOverride.Reason, priorityOverride.OverriddenBy, clock));
        }

        var path = FictionalPatientSeedValidator.PathTo(seed.Status);

        if (path.Count >= 1 && seed.TreatmentStartedMinutesAfterArrival is { } treatment)
        {
            yield return new Step(treatment, 1, (patient, clock) => patient.ChangeStatus(path[0], null, clock));
        }

        if (path.Count == 2 && seed.CompletedMinutesAfterArrival is { } completed)
        {
            yield return new Step(completed, 2, (patient, clock) => patient.ChangeStatus(path[1], seed.DispositionNote, clock));
        }
    }

    private sealed record Step(int MinutesAfterArrival, int Order, Action<Patient, TimeProvider> Apply);

    /// <summary>
    /// A <see cref="TimeProvider"/> (the project's clock abstraction) that the factory moves forward event by event, so
    /// each Domain call stamps the intended moment. The overall "now" comes from the injected application clock.
    /// </summary>
    private sealed class SteppingClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void MoveTo(DateTimeOffset moment) => _now = moment;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
