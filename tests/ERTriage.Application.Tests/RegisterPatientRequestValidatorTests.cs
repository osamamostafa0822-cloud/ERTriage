using ERTriage.Application.Common;
using ERTriage.Application.Patients.Requests;
using ERTriage.Application.Patients.Validation;

namespace ERTriage.Application.Tests;

public class RegisterPatientRequestValidatorTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    private static RegisterPatientRequest ValidRequest() =>
        new("Test Patient Alpha", 54, "Shortness of breath", 112, 138, 86, 37.9m, 88, Now);

    // Each case starts from a valid request and changes only what it names.
    private static readonly Dictionary<string, Func<RegisterPatientRequest, RegisterPatientRequest>> ValidCases = new()
    {
        ["age 0"] = r => r with { Age = 0 },
        ["age 120"] = r => r with { Age = 120 },
        ["heartRate 20"] = r => r with { HeartRate = 20 },
        ["heartRate 250"] = r => r with { HeartRate = 250 },
        ["systolicBp 50"] = r => r with { SystolicBp = 50, DiastolicBp = 40 },
        ["systolicBp 300"] = r => r with { SystolicBp = 300 },
        ["diastolicBp 20"] = r => r with { DiastolicBp = 20 },
        ["diastolicBp 200"] = r => r with { SystolicBp = 250, DiastolicBp = 200 },
        ["diastolic one below systolic"] = r => r with { SystolicBp = 120, DiastolicBp = 119 },
        ["temperature 30.0"] = r => r with { TemperatureC = 30.0m },
        ["temperature 45.0"] = r => r with { TemperatureC = 45.0m },
        ["temperature 36.8"] = r => r with { TemperatureC = 36.8m },
        ["spO2 50"] = r => r with { SpO2 = 50 },
        ["spO2 100"] = r => r with { SpO2 = 100 },
        ["name 2 chars"] = r => r with { FullName = "Al" },
        ["name 100 chars"] = r => r with { FullName = new string('a', 100) },
        ["name trimmed to 2 chars"] = r => r with { FullName = "  Al  " },
        ["complaint 2 chars"] = r => r with { ChiefComplaint = "Ok" },
        ["complaint 200 chars"] = r => r with { ChiefComplaint = new string('a', 200) },
        ["arrival exactly at tolerance"] = r => r with { ArrivalTime = Now.AddMinutes(5) },
        ["arrival in the past"] = r => r with { ArrivalTime = Now.AddHours(-3) }
    };

    private static readonly Dictionary<string, (Func<RegisterPatientRequest, RegisterPatientRequest> Change, string Field)> InvalidCases = new()
    {
        ["age -1"] = (r => r with { Age = -1 }, "age"),
        ["age 121"] = (r => r with { Age = 121 }, "age"),
        ["heartRate 19"] = (r => r with { HeartRate = 19 }, "heartRate"),
        ["heartRate 251"] = (r => r with { HeartRate = 251 }, "heartRate"),
        ["systolicBp 49"] = (r => r with { SystolicBp = 49, DiastolicBp = 40 }, "systolicBp"),
        ["systolicBp 301"] = (r => r with { SystolicBp = 301 }, "systolicBp"),
        ["diastolicBp 19"] = (r => r with { DiastolicBp = 19 }, "diastolicBp"),
        ["diastolicBp 201"] = (r => r with { SystolicBp = 250, DiastolicBp = 201 }, "diastolicBp"),
        ["diastolic equals systolic"] = (r => r with { SystolicBp = 100, DiastolicBp = 100 }, "diastolicBp"),
        ["diastolic above systolic"] = (r => r with { SystolicBp = 100, DiastolicBp = 110 }, "diastolicBp"),
        ["temperature 29.9"] = (r => r with { TemperatureC = 29.9m }, "temperatureC"),
        ["temperature 45.1"] = (r => r with { TemperatureC = 45.1m }, "temperatureC"),
        ["temperature two decimals"] = (r => r with { TemperatureC = 36.85m }, "temperatureC"),
        ["spO2 49"] = (r => r with { SpO2 = 49 }, "spO2"),
        ["spO2 101"] = (r => r with { SpO2 = 101 }, "spO2"),
        ["name 1 char"] = (r => r with { FullName = "A" }, "fullName"),
        ["name 101 chars"] = (r => r with { FullName = new string('a', 101) }, "fullName"),
        ["name blank"] = (r => r with { FullName = "   " }, "fullName"),
        ["name null"] = (r => r with { FullName = null }, "fullName"),
        ["complaint 1 char"] = (r => r with { ChiefComplaint = "A" }, "chiefComplaint"),
        ["complaint 201 chars"] = (r => r with { ChiefComplaint = new string('a', 201) }, "chiefComplaint"),
        ["complaint empty"] = (r => r with { ChiefComplaint = string.Empty }, "chiefComplaint"),
        ["age missing"] = (r => r with { Age = null }, "age"),
        ["heartRate missing"] = (r => r with { HeartRate = null }, "heartRate"),
        ["systolicBp missing"] = (r => r with { SystolicBp = null }, "systolicBp"),
        ["diastolicBp missing"] = (r => r with { DiastolicBp = null }, "diastolicBp"),
        ["temperature missing"] = (r => r with { TemperatureC = null }, "temperatureC"),
        ["spO2 missing"] = (r => r with { SpO2 = null }, "spO2"),
        ["arrival missing"] = (r => r with { ArrivalTime = null }, "arrivalTime"),
        ["arrival 6 minutes ahead"] = (r => r with { ArrivalTime = Now.AddMinutes(6) }, "arrivalTime"),
        ["arrival one tick over tolerance"] = (r => r with { ArrivalTime = Now.AddMinutes(5).AddTicks(1) }, "arrivalTime")
    };

    public static TheoryData<string> ValidCaseNames => ToData(ValidCases.Keys);

    public static TheoryData<string> InvalidCaseNames => ToData(InvalidCases.Keys);

    private static TheoryData<string> ToData(IEnumerable<string> keys)
    {
        var data = new TheoryData<string>();

        foreach (var key in keys)
        {
            data.Add(key);
        }

        return data;
    }

    [Fact]
    public void Validate_ValidRequest_ReturnsNoErrors() =>
        Assert.Empty(RegisterPatientRequestValidator.Validate(ValidRequest(), Now));

    [Theory]
    [MemberData(nameof(ValidCaseNames))]
    public void Validate_ValidBoundary_ReturnsNoErrors(string name) =>
        Assert.Empty(RegisterPatientRequestValidator.Validate(ValidCases[name](ValidRequest()), Now));

    [Theory]
    [MemberData(nameof(InvalidCaseNames))]
    public void Validate_InvalidValue_ReportsExactlyThatField(string name)
    {
        var (change, field) = InvalidCases[name];

        var errors = RegisterPatientRequestValidator.Validate(change(ValidRequest()), Now);

        var error = Assert.Single(errors);
        Assert.Equal(field, error.Field);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void Validate_NullRequest_ReturnsOneBodyError()
    {
        var error = Assert.Single(RegisterPatientRequestValidator.Validate(null, Now));

        Assert.Null(error.Field);
    }

    [Fact]
    public void Validate_EmptyRequest_ReportsEveryRequiredField()
    {
        var errors = RegisterPatientRequestValidator.Validate(new RegisterPatientRequest(null, null, null, null, null, null, null, null, null), Now);

        Assert.Equal(
            ["age", "arrivalTime", "chiefComplaint", "diastolicBp", "fullName", "heartRate", "spO2", "systolicBp", "temperatureC"],
            errors.Select(e => e.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Validate_SeveralProblems_ReportsAllOfThemAtOnce()
    {
        var request = new RegisterPatientRequest("A", 200, string.Empty, 5, 100, 100, 50.0m, 10, Now.AddHours(1));

        var errors = RegisterPatientRequestValidator.Validate(request, Now);

        Assert.Equal(
            ["age", "arrivalTime", "chiefComplaint", "diastolicBp", "fullName", "heartRate", "spO2", "temperatureC"],
            errors.Select(e => e.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Validate_PressureOutOfRange_SkipsTheDiastolicBelowSystolicComparison()
    {
        // 49 is below the systolic minimum; diastolic 60 is valid on its own. Only the range error is reported.
        var errors = RegisterPatientRequestValidator.Validate(ValidRequest() with { SystolicBp = 49, DiastolicBp = 60 }, Now);

        Assert.Equal("systolicBp", Assert.Single(errors).Field);
    }

    [Fact]
    public void Validate_Messages_NeverEchoTheSuppliedValues()
    {
        // Both values exceed their maximum (name 100, complaint 200), so both are rejected.
        var request = ValidRequest() with { FullName = new string('s', 120), ChiefComplaint = new string('s', 250) };

        var errors = RegisterPatientRequestValidator.Validate(request, Now);

        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.DoesNotContain("sss", e.Message));
    }

    [Fact]
    public void EnsureValid_InvalidRequest_ThrowsValidationExceptionWithTheErrors()
    {
        var exception = Assert.Throws<ValidationException>(
            () => RegisterPatientRequestValidator.EnsureValid(ValidRequest() with { Age = 121, SpO2 = 49 }, Now));

        Assert.Equal(["age", "spO2"], exception.Errors.Select(e => e.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EnsureValid_ValidRequest_DoesNotThrow() =>
        RegisterPatientRequestValidator.EnsureValid(ValidRequest(), Now);
}
