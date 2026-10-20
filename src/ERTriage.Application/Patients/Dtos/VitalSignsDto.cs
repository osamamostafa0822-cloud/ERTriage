namespace ERTriage.Application.Patients.Dtos;

public sealed record VitalSignsDto(int HeartRate, int SystolicBp, int DiastolicBp, decimal TemperatureC, int SpO2);
