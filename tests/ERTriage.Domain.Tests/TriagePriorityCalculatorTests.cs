using ERTriage.Domain.Triage;

namespace ERTriage.Domain.Tests;

public class TriagePriorityCalculatorTests
{
    [Theory]
    [InlineData(50, 1)]
    [InlineData(89, 1)]
    [InlineData(90, 2)]
    [InlineData(93, 2)]
    [InlineData(94, 3)]
    [InlineData(95, 3)]
    [InlineData(96, 4)]
    [InlineData(100, 4)]
    public void SpO2_Boundaries_MapToExpectedPriority(int spO2, int expected) =>
        Assert.Equal(expected, TriagePriorityCalculator.Calculate(TestData.Vitals(spO2: spO2)).Priority);

    [Theory]
    [InlineData(20, 1)]
    [InlineData(39, 1)]
    [InlineData(40, 2)]
    [InlineData(49, 2)]
    [InlineData(50, 3)]
    [InlineData(59, 3)]
    [InlineData(60, 4)]
    [InlineData(100, 4)]
    [InlineData(101, 3)]
    [InlineData(120, 3)]
    [InlineData(121, 2)]
    [InlineData(140, 2)]
    [InlineData(141, 1)]
    [InlineData(250, 1)]
    public void HeartRate_Boundaries_MapToExpectedPriority(int heartRate, int expected) =>
        Assert.Equal(expected, TriagePriorityCalculator.Calculate(TestData.Vitals(heartRate: heartRate)).Priority);

    [Theory]
    [InlineData(50, 1)]
    [InlineData(79, 1)]
    [InlineData(80, 2)]
    [InlineData(89, 2)]
    [InlineData(90, 3)]
    [InlineData(99, 3)]
    [InlineData(100, 4)]
    [InlineData(159, 4)]
    [InlineData(160, 3)]
    [InlineData(179, 3)]
    [InlineData(180, 2)]
    [InlineData(219, 2)]
    [InlineData(220, 1)]
    [InlineData(300, 1)]
    public void SystolicBp_Boundaries_MapToExpectedPriority(int systolic, int expected) =>
        Assert.Equal(expected, TriagePriorityCalculator.Calculate(TestData.Vitals(systolicBp: systolic)).Priority);

    [Theory]
    [InlineData(30.0, 1)]
    [InlineData(34.9, 1)]
    [InlineData(35.0, 2)]
    [InlineData(35.4, 2)]
    [InlineData(35.5, 3)]
    [InlineData(35.9, 3)]
    [InlineData(36.0, 4)]
    [InlineData(37.9, 4)]
    [InlineData(38.0, 3)]
    [InlineData(39.4, 3)]
    [InlineData(39.5, 2)]
    [InlineData(40.9, 2)]
    [InlineData(41.0, 1)]
    [InlineData(45.0, 1)]
    public void Temperature_Boundaries_MapToExpectedPriority(double temperature, int expected) =>
        Assert.Equal(expected, TriagePriorityCalculator.Calculate(TestData.Vitals(temperatureC: (decimal)temperature)).Priority);

    [Theory]
    [InlineData(88, 80, 120, 37.0, 1, "SpO2 88% < 90")]
    [InlineData(90, 80, 120, 37.0, 2, "SpO2 90% in 90–93")]
    [InlineData(97, 130, 120, 38.2, 2, "HR 130 in 121–140")]
    [InlineData(95, 72, 125, 36.8, 3, "SpO2 95% in 94–95")]
    [InlineData(98, 75, 120, 36.8, 4, TriagePriorityCalculator.AllNormalReason)]
    [InlineData(85, 150, 70, 34.5, 1, "SpO2 85% < 90; HR 150 > 140; SBP 70 < 80; Temp 34.5 < 35.0")]
    [InlineData(91, 130, 120, 37.0, 2, "SpO2 91% in 90–93; HR 130 in 121–140")]
    [InlineData(98, 80, 120, 41.0, 1, "Temp 41.0 ≥ 41.0")]
    [InlineData(98, 80, 230, 37.0, 1, "SBP 230 ≥ 220")]
    public void Calculate_WorkedExamples_ReturnPriorityAndReason(
        int spO2, int heartRate, int systolic, double temperature, int expectedPriority, string expectedReason)
    {
        var vitals = TestData.Vitals(heartRate, systolic, (decimal)temperature, spO2);

        var assessment = TriagePriorityCalculator.Calculate(vitals);

        Assert.Equal(expectedPriority, assessment.Priority);
        Assert.Equal(expectedReason, assessment.Reason);
    }

    [Fact]
    public void Calculate_MostUrgentBandWins()
    {
        // SpO2 band 4, HR band 2, SBP band 4, Temp band 3 => 2
        var assessment = TriagePriorityCalculator.Calculate(TestData.Vitals(heartRate: 130, temperatureC: 38.2m, spO2: 97));

        Assert.Equal(2, assessment.Priority);
    }

    [Fact]
    public void Calculate_DiastolicPressure_DoesNotAffectResult()
    {
        var low = TriagePriorityCalculator.Calculate(TestData.Vitals(diastolicBp: 20));
        var high = TriagePriorityCalculator.Calculate(TestData.Vitals(diastolicBp: 119));

        Assert.Equal(low, high);
    }

    [Fact]
    public void Calculate_SameInput_SameOutput()
    {
        var vitals = TestData.Vitals(heartRate: 130, spO2: 91);

        Assert.Equal(TriagePriorityCalculator.Calculate(vitals), TriagePriorityCalculator.Calculate(vitals));
    }

    [Fact]
    public void Calculate_WorstCaseReason_FitsPersistedLength()
    {
        var assessment = TriagePriorityCalculator.Calculate(TestData.Vitals(heartRate: 250, systolicBp: 300, temperatureC: 45.0m, spO2: 50));

        Assert.Equal(1, assessment.Priority);
        Assert.InRange(assessment.Reason.Length, 1, 200);
    }

    [Fact]
    public void Calculate_NullVitals_Throws() =>
        Assert.Throws<ArgumentNullException>(() => TriagePriorityCalculator.Calculate(null!));
}
