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
            return [new ApiError(null, "Request body is required.")];
        }

        var errors = new FieldErrors();

        errors.RequiredText("fullName", "Full name", request.FullName, PatientRules.FullNameMinLength, PatientRules.FullNameMaxLength);
        errors.Range("age", "Age", request.Age, PatientRules.AgeMin, PatientRules.AgeMax, " years");
        errors.RequiredText("chiefComplaint", "Chief complaint", request.ChiefComplaint, PatientRules.ChiefComplaintMinLength, PatientRules.ChiefComplaintMaxLength);

        errors.Range("heartRate", "Heart rate", request.HeartRate, VitalSignsLimits.HeartRateMin, VitalSignsLimits.HeartRateMax, " bpm");
        var systolicValid = errors.Range("systolicBp", "Systolic blood pressure", request.SystolicBp, VitalSignsLimits.SystolicBpMin, VitalSignsLimits.SystolicBpMax, " mmHg");
        var diastolicValid = errors.Range("diastolicBp", "Diastolic blood pressure", request.DiastolicBp, VitalSignsLimits.DiastolicBpMin, VitalSignsLimits.DiastolicBpMax, " mmHg");

        if (systolicValid && diastolicValid && request.DiastolicBp >= request.SystolicBp)
        {
            errors.Add("diastolicBp", "Diastolic blood pressure must be lower than systolic blood pressure.");
        }

        var temperatureValid = errors.Range("temperatureC", "Temperature", request.TemperatureC, VitalSignsLimits.TemperatureCMin, VitalSignsLimits.TemperatureCMax, " °C");

        if (temperatureValid && request.TemperatureC is { } temperature && decimal.Round(temperature, 1) != temperature)
        {
            errors.Add("temperatureC", "Temperature must have at most one decimal place.");
        }

        errors.Range("spO2", "SpO2", request.SpO2, VitalSignsLimits.SpO2Min, VitalSignsLimits.SpO2Max, " %");

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
            errors.Add("arrivalTime", "Arrival time is required.");
            return;
        }

        if (arrival > now + PatientRules.ArrivalFutureTolerance)
        {
            errors.Add(
                "arrivalTime",
                FormattableString.Invariant(
                    $"Arrival time cannot be more than {(int)PatientRules.ArrivalFutureTolerance.TotalMinutes} minutes in the future."));
        }
    }
}
