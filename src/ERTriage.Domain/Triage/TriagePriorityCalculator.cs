using System.Globalization;
using ERTriage.Domain.Patients;

namespace ERTriage.Domain.Triage;

/// <summary>
/// Pure, deterministic priority calculation. ILLUSTRATIVE and NOT CLINICALLY VALIDATED
/// (thresholds: <see cref="TriageThresholds"/>).
/// The most urgent band among SpO2, heart rate, systolic BP and temperature wins; DBP and age are not scored.
/// </summary>
public static class TriagePriorityCalculator
{
    public const string AllNormalReason = "All scored vitals within normal bands";

    private readonly record struct Finding(int Priority, string Text);

    public static TriageAssessment Calculate(VitalSigns vitals)
    {
        ArgumentNullException.ThrowIfNull(vitals);

        // Fixed order, also the order used in the reason text: SpO2, HR, SBP, Temp.
        Finding[] findings =
        [
            Classify("SpO2", vitals.SpO2, FormattableString.Invariant($"{vitals.SpO2}%"), TriageThresholds.SpO2),
            Classify("HR", vitals.HeartRate, FormattableString.Invariant($"{vitals.HeartRate}"), TriageThresholds.HeartRate),
            Classify("SBP", vitals.SystolicBp, FormattableString.Invariant($"{vitals.SystolicBp}"), TriageThresholds.SystolicBp),
            Classify(
                "Temp",
                vitals.TemperatureC,
                vitals.TemperatureC.ToString("0.0", CultureInfo.InvariantCulture),
                TriageThresholds.Temperature)
        ];

        var priority = findings.Min(f => f.Priority);

        if (priority == TriagePriority.LeastUrgent)
        {
            return new TriageAssessment(priority, AllNormalReason);
        }

        var reason = string.Join("; ", findings.Where(f => f.Priority == priority).Select(f => f.Text));
        return new TriageAssessment(priority, reason);
    }

    private static Finding Classify(string name, decimal value, string valueText, IReadOnlyList<PriorityBand> bands)
    {
        // The bands cover the whole decimal range, so a match always exists.
        var band = bands.First(b => b.Contains(value));
        return new Finding(band.Priority, $"{name} {valueText} {band.Description}");
    }
}
