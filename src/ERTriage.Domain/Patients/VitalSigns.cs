using ERTriage.Domain.Common;

namespace ERTriage.Domain.Patients;

/// <summary>
/// Value object (owned by <see cref="Patient"/>): the five readings taken at intake. Immutable once created
/// and always valid - the only way to get one is <see cref="Create"/>, which enforces
/// <see cref="VitalSignsLimits"/> and DBP &lt; SBP.
/// </summary>
public sealed record VitalSigns
{
    private VitalSigns()
    {
    }

    public int HeartRate { get; private set; }

    public int SystolicBp { get; private set; }

    public int DiastolicBp { get; private set; }

    public decimal TemperatureC { get; private set; }

    public int SpO2 { get; private set; }

    public static VitalSigns Create(int heartRate, int systolicBp, int diastolicBp, decimal temperatureC, int spO2)
    {
        Guard.InRange(heartRate, VitalSignsLimits.HeartRateMin, VitalSignsLimits.HeartRateMax, "Heart rate", " bpm");
        Guard.InRange(systolicBp, VitalSignsLimits.SystolicBpMin, VitalSignsLimits.SystolicBpMax, "Systolic blood pressure", " mmHg");
        Guard.InRange(diastolicBp, VitalSignsLimits.DiastolicBpMin, VitalSignsLimits.DiastolicBpMax, "Diastolic blood pressure", " mmHg");
        Guard.InRange(temperatureC, VitalSignsLimits.TemperatureCMin, VitalSignsLimits.TemperatureCMax, "Temperature", " °C");
        Guard.InRange(spO2, VitalSignsLimits.SpO2Min, VitalSignsLimits.SpO2Max, "SpO2", " %");

        if (diastolicBp >= systolicBp)
        {
            throw new DomainValidationException("Diastolic blood pressure must be lower than systolic blood pressure.");
        }

        if (decimal.Round(temperatureC, 1) != temperatureC)
        {
            throw new DomainValidationException("Temperature must have at most one decimal place.");
        }

        return new VitalSigns
        {
            HeartRate = heartRate,
            SystolicBp = systolicBp,
            DiastolicBp = diastolicBp,
            TemperatureC = temperatureC,
            SpO2 = spO2
        };
    }
}
