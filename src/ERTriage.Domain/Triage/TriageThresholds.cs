namespace ERTriage.Domain.Triage;

/// <summary>
/// ILLUSTRATIVE triage bands - NOT CLINICALLY VALIDATED. For demonstration only; never use for patient care.
/// The only value taken from the challenge PDF is SpO2 below 90% => priority 1; every other band is our
/// own design (see docs/03-features/triage-priority.md). This is the single place thresholds live: changing
/// one is a one-line change here plus a test update.
///
/// Bands are half-open [From, To), contiguous and non-overlapping over the whole decimal range, so every
/// value maps to exactly one band per vital. Input plausibility limits are separate (<c>VitalSignsLimits</c>).
/// </summary>
public static class TriageThresholds
{
    private const decimal Min = decimal.MinValue;
    private const decimal Max = decimal.MaxValue;

    /// <summary>SpO2 (%). Below 90 => priority 1 (challenge PDF).</summary>
    public static IReadOnlyList<PriorityBand> SpO2 { get; } =
    [
        new(1, Min, 90m, "< 90"),
        new(2, 90m, 94m, "in 90–93"),
        new(3, 94m, 96m, "in 94–95"),
        new(4, 96m, Max, "in 96–100")
    ];

    /// <summary>Heart rate (bpm).</summary>
    public static IReadOnlyList<PriorityBand> HeartRate { get; } =
    [
        new(1, Min, 40m, "< 40"),
        new(1, 141m, Max, "> 140"),
        new(2, 40m, 50m, "in 40–49"),
        new(2, 121m, 141m, "in 121–140"),
        new(3, 50m, 60m, "in 50–59"),
        new(3, 101m, 121m, "in 101–120"),
        new(4, 60m, 101m, "in 60–100")
    ];

    /// <summary>Systolic blood pressure (mmHg).</summary>
    public static IReadOnlyList<PriorityBand> SystolicBp { get; } =
    [
        new(1, Min, 80m, "< 80"),
        new(1, 220m, Max, "≥ 220"),
        new(2, 80m, 90m, "in 80–89"),
        new(2, 180m, 220m, "in 180–219"),
        new(3, 90m, 100m, "in 90–99"),
        new(3, 160m, 180m, "in 160–179"),
        new(4, 100m, 160m, "in 100–159")
    ];

    /// <summary>Body temperature (°C), half-open intervals.</summary>
    public static IReadOnlyList<PriorityBand> Temperature { get; } =
    [
        new(1, Min, 35.0m, "< 35.0"),
        new(1, 41.0m, Max, "≥ 41.0"),
        new(2, 35.0m, 35.5m, "in [35.0, 35.5)"),
        new(2, 39.5m, 41.0m, "in [39.5, 41.0)"),
        new(3, 35.5m, 36.0m, "in [35.5, 36.0)"),
        new(3, 38.0m, 39.5m, "in [38.0, 39.5)"),
        new(4, 36.0m, 38.0m, "in [36.0, 38.0)")
    ];
}
