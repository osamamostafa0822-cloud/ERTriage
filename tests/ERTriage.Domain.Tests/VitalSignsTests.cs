using ERTriage.Domain.Common;
using ERTriage.Domain.Patients;

namespace ERTriage.Domain.Tests;

public class VitalSignsTests
{
    [Fact]
    public void Create_ValidReadings_ExposesValues()
    {
        var vitals = VitalSigns.Create(112, 138, 86, 37.9m, 88);

        Assert.Equal(112, vitals.HeartRate);
        Assert.Equal(138, vitals.SystolicBp);
        Assert.Equal(86, vitals.DiastolicBp);
        Assert.Equal(37.9m, vitals.TemperatureC);
        Assert.Equal(88, vitals.SpO2);
    }

    [Fact]
    public void Create_LowerLimits_Accepted() =>
        VitalSigns.Create(
            VitalSignsLimits.HeartRateMin,
            VitalSignsLimits.SystolicBpMin,
            VitalSignsLimits.DiastolicBpMin,
            VitalSignsLimits.TemperatureCMin,
            VitalSignsLimits.SpO2Min);

    [Fact]
    public void Create_UpperLimits_Accepted() =>
        VitalSigns.Create(
            VitalSignsLimits.HeartRateMax,
            VitalSignsLimits.SystolicBpMax,
            VitalSignsLimits.DiastolicBpMax,
            VitalSignsLimits.TemperatureCMax,
            VitalSignsLimits.SpO2Max);

    [Theory]
    [InlineData(19)]
    [InlineData(251)]
    public void Create_HeartRateOutOfRange_Throws(int heartRate) =>
        Assert.Throws<DomainValidationException>(() => VitalSigns.Create(heartRate, 120, 80, 37.0m, 98));

    [Theory]
    [InlineData(49)]
    [InlineData(301)]
    public void Create_SystolicOutOfRange_Throws(int systolic) =>
        Assert.Throws<DomainValidationException>(() => VitalSigns.Create(80, systolic, 40, 37.0m, 98));

    [Theory]
    [InlineData(19)]
    [InlineData(201)]
    public void Create_DiastolicOutOfRange_Throws(int diastolic) =>
        Assert.Throws<DomainValidationException>(() => VitalSigns.Create(80, 300, diastolic, 37.0m, 98));

    [Theory]
    [InlineData(29.9)]
    [InlineData(45.1)]
    public void Create_TemperatureOutOfRange_Throws(double temperature) =>
        Assert.Throws<DomainValidationException>(() => VitalSigns.Create(80, 120, 80, (decimal)temperature, 98));

    [Theory]
    [InlineData(49)]
    [InlineData(101)]
    public void Create_SpO2OutOfRange_Throws(int spO2) =>
        Assert.Throws<DomainValidationException>(() => VitalSigns.Create(80, 120, 80, 37.0m, spO2));

    [Theory]
    [InlineData(120, 120)]
    [InlineData(120, 121)]
    public void Create_DiastolicNotBelowSystolic_Throws(int systolic, int diastolic) =>
        Assert.Throws<DomainValidationException>(() => VitalSigns.Create(80, systolic, diastolic, 37.0m, 98));

    [Fact]
    public void Create_DiastolicJustBelowSystolic_Accepted() =>
        VitalSigns.Create(80, 120, 119, 37.0m, 98);

    [Fact]
    public void Create_TemperatureWithTwoDecimals_Throws() =>
        Assert.Throws<DomainValidationException>(() => VitalSigns.Create(80, 120, 80, 37.55m, 98));

    [Fact]
    public void Create_TemperatureWithTrailingZero_Accepted() =>
        VitalSigns.Create(80, 120, 80, 37.50m, 98);

    [Fact]
    public void Create_InvalidReading_MessageDoesNotEchoTheValue()
    {
        var exception = Assert.Throws<DomainValidationException>(() => VitalSigns.Create(251, 120, 80, 37.0m, 98));

        Assert.DoesNotContain("251", exception.Message);
    }

    [Fact]
    public void Equality_SameReadings_AreEqual() =>
        Assert.Equal(VitalSigns.Create(80, 120, 80, 37.0m, 98), VitalSigns.Create(80, 120, 80, 37.0m, 98));
}
