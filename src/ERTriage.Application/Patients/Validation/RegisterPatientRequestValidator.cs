using ERTriage.Domain.Common;
using ERTriage.Application.Common;
using ERTriage.Application.Patients.Requests;
using ERTriage.Domain.Patients;

namespace ERTriage.Application.Patients.Validation;

/// <summary>
/// Collects every field error of a registration request (camelCase field names). Limits come from
/// <see cref="PatientRules"/> and <see cref="VitalSignsLimits"/>; the Domain (<c>Patient.Register</c>) still enforces
/// them as the final authority. The DBP &lt; SBP check runs only when both values are individually valid.
/// </summary>
public static class RegisterPatientRequestValidator
{
    public static IReadOnlyList<ApiError> Validate(RegisterPatientRequest? request, DateTimeOffset now)
    {
        if (request is null)
        {
            return [new ApiError(null, MessageCatalog.Get("BodyRequired"))];
        }

        var errors = new FieldErrors();

        errors.RequiredText("fullName", MessageCatalog.Get("FullName"), request.FullName, PatientRules.FullNameMinLength, PatientRules.FullNameMaxLength);
        errors.Range("age", MessageCatalog.Get("Age"), request.Age, PatientRules.AgeMin, PatientRules.AgeMax, MessageCatalog.Get("Years"));
        errors.RequiredText("chiefComplaint", MessageCatalog.Get("ChiefComplaint"), request.ChiefComplaint, PatientRules.ChiefComplaintMinLength, PatientRules.ChiefComplaintMaxLength);

        errors.Range("heartRate", MessageCatalog.Get("HeartRate"), request.HeartRate, VitalSignsLimits.HeartRateMin, VitalSignsLimits.HeartRateMax, MessageCatalog.Get("Bpm"));
        var systolicValid = errors.Range("systolicBp", MessageCatalog.Get("SystolicBp"), request.SystolicBp, VitalSignsLimits.SystolicBpMin, VitalSignsLimits.SystolicBpMax, MessageCatalog.Get("MmHg"));
        var diastolicValid = errors.Range("diastolicBp", MessageCatalog.Get("DiastolicBp"), request.DiastolicBp, VitalSignsLimits.DiastolicBpMin, VitalSignsLimits.DiastolicBpMax, MessageCatalog.Get("MmHg"));

        if (systolicValid && diastolicValid && request.DiastolicBp >= request.SystolicBp)
        {
            errors.Add("diastolicBp", MessageCatalog.Get("BpOrder"));
        }

        var temperatureValid = errors.Range("temperatureC", MessageCatalog.Get("Temperature"), request.TemperatureC, VitalSignsLimits.TemperatureCMin, VitalSignsLimits.TemperatureCMax, MessageCatalog.Get("Celsius"));

        if (temperatureValid && request.TemperatureC is { } temperature && decimal.Round(temperature, 1) != temperature)
        {
            errors.Add("temperatureC", MessageCatalog.Get("TemperaturePrecision"));
        }

        errors.Range("spO2", MessageCatalog.Get("SpO2"), request.SpO2, VitalSignsLimits.SpO2Min, VitalSignsLimits.SpO2Max, MessageCatalog.Get("Percent"));

        ValidateArrivalTime(request.ArrivalTime, now, errors);

        return errors.ToList();
    }

    public static void EnsureValid(RegisterPatientRequest? request, DateTimeOffset now)
    {
        var errors = Validate(request, now);

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static void ValidateArrivalTime(DateTimeOffset? arrivalTime, DateTimeOffset now, FieldErrors errors)
    {
        if (arrivalTime is not { } arrival)
        {
            errors.Add("arrivalTime", MessageCatalog.Get("ArrivalRequired"));
            return;
        }

        if (arrival > now + PatientRules.ArrivalFutureTolerance)
        {
            errors.Add(
                "arrivalTime",
                MessageCatalog.Format("ArrivalFuture", (int)PatientRules.ArrivalFutureTolerance.TotalMinutes));
        }
    }
}
