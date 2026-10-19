using ERTriage.Domain.Patients;
using ERTriage.Domain.Triage;

namespace ERTriage.Domain.Tests;

/// <summary>Structural checks on the illustrative band tables (not clinical validation).</summary>
public class TriageThresholdsTests
{
    private static void AssertExactlyOneBand(IReadOnlyList<PriorityBand> bands, decimal value)
    {
        var matches = bands.Where(b => b.Contains(value)).ToList();

        Assert.Single(matches);
        Assert.True(TriagePriority.IsValid(matches[0].Priority));
    }

    [Fact]
    public void SpO2Bands_EveryValidValue_FallsInExactlyOneBand()
    {
        for (var v = VitalSignsLimits.SpO2Min; v <= VitalSignsLimits.SpO2Max; v++)
        {
            AssertExactlyOneBand(TriageThresholds.SpO2, v);
        }
    }

    [Fact]
    public void HeartRateBands_EveryValidValue_FallsInExactlyOneBand()
    {
        for (var v = VitalSignsLimits.HeartRateMin; v <= VitalSignsLimits.HeartRateMax; v++)
        {
            AssertExactlyOneBand(TriageThresholds.HeartRate, v);
        }
    }

    [Fact]
    public void SystolicBands_EveryValidValue_FallsInExactlyOneBand()
    {
        for (var v = VitalSignsLimits.SystolicBpMin; v <= VitalSignsLimits.SystolicBpMax; v++)
        {
            AssertExactlyOneBand(TriageThresholds.SystolicBp, v);
        }
    }

    [Fact]
    public void TemperatureBands_EveryValidTenth_FallsInExactlyOneBand()
    {
        for (var tenths = 300; tenths <= 450; tenths++)
        {
            AssertExactlyOneBand(TriageThresholds.Temperature, tenths / 10m);
        }
    }

    [Fact]
    public void AllBandTables_ContainEveryPriorityAndNoInvalidOnes()
    {
        foreach (var bands in new[] { TriageThresholds.SpO2, TriageThresholds.HeartRate, TriageThresholds.SystolicBp, TriageThresholds.Temperature })
        {
            Assert.Equal(new[] { 1, 2, 3, 4 }, bands.Select(b => b.Priority).Distinct().OrderBy(p => p).ToArray());
            Assert.All(bands, b => Assert.True(b.From < b.To));
        }
    }

    [Fact]
    public void SpO2_BelowNinety_IsPriorityOne_AsInChallengePdf() =>
        Assert.Equal(1, TriageThresholds.SpO2.First(b => b.Contains(89m)).Priority);
}
