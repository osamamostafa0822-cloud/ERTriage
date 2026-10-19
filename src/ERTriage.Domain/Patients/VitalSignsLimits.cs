namespace ERTriage.Domain.Patients;

/// <summary>
/// Plausibility limits for vital-sign INPUT. They reject typos and impossible readings; they are NOT clinical
/// thresholds (those are in <c>TriageThresholds</c>). Each range covers every classification band.
/// Values are our design (docs/03-features/patient-registration.md), not clinically validated.
/// </summary>
public static class VitalSignsLimits
{
    public const int HeartRateMin = 20;
    public const int HeartRateMax = 250;

    public const int SystolicBpMin = 50;
    public const int SystolicBpMax = 300;

    public const int DiastolicBpMin = 20;
    public const int DiastolicBpMax = 200;

    public const decimal TemperatureCMin = 30.0m;
    public const decimal TemperatureCMax = 45.0m;

    public const int SpO2Min = 50;
    public const int SpO2Max = 100;
}
