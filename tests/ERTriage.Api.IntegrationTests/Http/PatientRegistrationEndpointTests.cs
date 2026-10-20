using System.Net;
using System.Net.Http.Json;
using ERTriage.Api.IntegrationTests.Persistence;

namespace ERTriage.Api.IntegrationTests.Http;

/// <summary><c>POST /api/patients</c> and <c>GET /api/patients/{id}</c> over real HTTP and a real database.</summary>
public sealed class PatientRegistrationEndpointTests(ApiTestFixture fixture)
    : HttpTestBase(fixture), IClassFixture<ApiTestFixture>
{
    private static readonly string[] AllRequiredFields =
        ["fullName", "age", "chiefComplaint", "heartRate", "systolicBp", "diastolicBp", "temperatureC", "spO2", "arrivalTime"];

    [SqlServerFact]
    public async Task Register_ValidRequest_Returns201WithLocationAndPatient()
    {
        var arrival = Now.AddMinutes(-20);

        using var response = await Client.PostAsJsonAsync(PatientsRoute, PatientBody("Alice Fictional", spO2: 85, arrival: arrival));
        var data = await AssertSuccessAsync(response, HttpStatusCode.Created);

        var id = data.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal($"{PatientsRoute}/{id}", response.Headers.Location?.AbsolutePath);
        Assert.Equal("Alice Fictional", data.GetProperty("fullName").GetString());
        Assert.Equal(40, data.GetProperty("age").GetInt32());
        Assert.Equal(arrival, data.GetProperty("arrivalTime").GetDateTimeOffset());
        Assert.Equal(1, data.GetProperty("calculatedPriority").GetInt32()); // SpO2 85 < 90
        Assert.Equal(1, data.GetProperty("currentPriority").GetInt32());
        Assert.False(data.GetProperty("isOverridden").GetBoolean());
        Assert.Equal("Waiting", data.GetProperty("status").GetString());
        Assert.Equal(new[] { "InTreatment" }, Strings(data.GetProperty("allowedTransitions")));
        Assert.False(data.TryGetProperty("rowVersion", out _)); // concurrency token never exposed (D-42)
    }

    [SqlServerFact]
    public async Task Register_ThenGetLocation_Returns200WithTheSamePatient()
    {
        using var created = await Client.PostAsJsonAsync(PatientsRoute, PatientBody("Bob Fictional"));
        var id = (await AssertSuccessAsync(created, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        using var response = await Client.GetAsync(created.Headers.Location);
        var data = await AssertSuccessAsync(response, HttpStatusCode.OK);

        Assert.Equal(id, data.GetProperty("id").GetGuid());
        Assert.Equal("Bob Fictional", data.GetProperty("fullName").GetString());
        Assert.Equal(4, data.GetProperty("currentPriority").GetInt32());
    }

    [SqlServerFact]
    public async Task Register_EmptyObject_Returns400WithEveryMissingFieldAndSavesNothing()
    {
        using var response = await SendRawJsonAsync(HttpMethod.Post, PatientsRoute, "{}");
        var body = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);

        Assert.Equal(AllRequiredFields.Order(), ErrorFields(body).Order());
        Assert.Equal(0, (await GetBoardAsync()).GetProperty("totalCount").GetInt32());
    }

    public static TheoryData<string, object?> InvalidFieldValues => new()
    {
        { "fullName", "A" },
        { "fullName", "   " },
        { "age", -1 },
        { "age", 121 },
        { "chiefComplaint", "" },
        { "heartRate", 19 },
        { "heartRate", 251 },
        { "systolicBp", 301 },
        { "diastolicBp", 120 },            // equal to systolic 120: diastolic must be lower
        { "temperatureC", 37.25m },        // more than one decimal
        { "temperatureC", 45.1m },
        { "spO2", 49 },
        { "spO2", 101 },
        { "arrivalTime", Now.AddMinutes(6) } // more than 5 minutes in the future
    };

    [SqlServerTheory]
    [MemberData(nameof(InvalidFieldValues))]
    public async Task Register_InvalidField_Returns400ForThatFieldOnly(string field, object? value)
    {
        var body = PatientBody();
        body[field] = value;

        using var response = await Client.PostAsJsonAsync(PatientsRoute, body);
        var result = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);

        Assert.Equal(new[] { field }, ErrorFields(result));
    }

    [SqlServerFact]
    public async Task Register_ArrivalExactlyFiveMinutesAhead_IsAccepted()
    {
        using var response = await Client.PostAsJsonAsync(PatientsRoute, PatientBody(arrival: Now.AddMinutes(5)));

        await AssertSuccessAsync(response, HttpStatusCode.Created);
    }

    [SqlServerFact]
    public async Task Register_MissingBody_Returns400Envelope()
    {
        using var response = await SendRawJsonAsync(HttpMethod.Post, PatientsRoute, "");

        var body = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);
        Assert.NotEmpty(ErrorFields(body));
    }

    [SqlServerFact]
    public async Task Register_RejectedValue_IsNeverEchoedInTheResponse()
    {
        const string marker = "SECRET-FICTIONAL-NAME-";
        var body = PatientBody(marker + new string('x', 120)); // longer than 100 characters

        using var response = await Client.PostAsJsonAsync(PatientsRoute, body);
        var result = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);

        Assert.Equal(new[] { "fullName" }, ErrorFields(result));
        Assert.DoesNotContain(marker, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task GetById_UnknownPatient_Returns404Envelope()
    {
        using var response = await GetPatientAsync(Guid.NewGuid());

        var body = await AssertFailureAsync(response, HttpStatusCode.NotFound, PatientNotFound);
        Assert.Empty(body.GetProperty("errors").EnumerateArray());
    }

    [SqlServerTheory]
    [InlineData("not-a-guid")]
    [InlineData("zz-not-a-guid-zz")]
    public async Task GetById_IdIsNotAGuid_Returns400ForId(string id)
    {
        using var response = await GetPatientAsync(id);

        var body = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);
        Assert.Equal(new[] { "id" }, ErrorFields(body));
        Assert.Equal(new[] { InvalidRequest }, ErrorMessages(body));
        Assert.DoesNotContain(id, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
