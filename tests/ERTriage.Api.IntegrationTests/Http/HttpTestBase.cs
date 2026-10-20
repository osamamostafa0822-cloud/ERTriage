using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ERTriage.Api.IntegrationTests.Http;

/// <summary>
/// Shared helpers for the HTTP tests. Each test starts with an empty Patients table in its class's own database and
/// a fresh client. All data is fictional.
/// </summary>
public abstract class HttpTestBase(ApiTestFixture fixture) : IAsyncLifetime
{
    protected const string PatientsRoute = "/api/patients";
    protected const string BoardRoute = "/api/triage-board";
    protected const string ValidationFailed = "Validation failed.";
    protected const string InvalidRequest = "The request contains an invalid value.";
    protected const string PatientNotFound = "Patient was not found.";

    protected static readonly DateTimeOffset Now = ApiTestFixture.Now;

    private HttpClient? _client;

    protected ApiTestFixture Fixture { get; } = fixture;

    protected HttpClient Client => _client ?? throw new InvalidOperationException("Not initialized.");

    public async Task InitializeAsync()
    {
        await Fixture.ResetDatabaseAsync();
        _client = Fixture.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>A valid registration body (all vitals normal: priority 4 unless <paramref name="spO2"/> says otherwise).</summary>
    /// <remarks>SpO2 85 gives 1, 92 gives 2, 95 gives 3, 98 gives 4 (illustrative bands, triage-priority.md).</remarks>
    protected static Dictionary<string, object?> PatientBody(
        string name = "Test Patient",
        int spO2 = 98,
        DateTimeOffset? arrival = null) =>
        new()
        {
            ["fullName"] = name,
            ["age"] = 40,
            ["chiefComplaint"] = "Chest pain",
            ["heartRate"] = 80,
            ["systolicBp"] = 120,
            ["diastolicBp"] = 80,
            ["temperatureC"] = 36.8m,
            ["spO2"] = spO2,
            ["arrivalTime"] = arrival ?? Now
        };

    protected async Task<JsonElement> RegisterAsync(Dictionary<string, object?> body)
    {
        using var response = await Client.PostAsJsonAsync(PatientsRoute, body);
        return await AssertSuccessAsync(response, HttpStatusCode.Created);
    }

    protected async Task<Guid> RegisterIdAsync(string name = "Test Patient", int spO2 = 98, DateTimeOffset? arrival = null) =>
        (await RegisterAsync(PatientBody(name, spO2, arrival))).GetProperty("id").GetGuid();

    protected Task<HttpResponseMessage> GetPatientAsync(object id) => Client.GetAsync($"{PatientsRoute}/{id}");

    protected Task<HttpResponseMessage> ChangeStatusAsync(Guid id, string targetStatus, string? dispositionNote = null) =>
        Client.PostAsJsonAsync($"{PatientsRoute}/{id}/status", new { targetStatus, dispositionNote });

    protected Task<HttpResponseMessage> OverrideAsync(Guid id, int? priority, string? reason, string? overriddenBy = "Nurse Test") =>
        Client.PutAsJsonAsync($"{PatientsRoute}/{id}/priority-override", new { priority, reason, overriddenBy });

    /// <summary>Moves a Waiting patient to InTreatment and, for Discharged/Admitted, on to the final status.</summary>
    protected async Task MoveToAsync(Guid id, string status)
    {
        using (var toTreatment = await ChangeStatusAsync(id, "InTreatment"))
        {
            Assert.Equal(HttpStatusCode.OK, toTreatment.StatusCode);
        }

        if (status is "Discharged" or "Admitted")
        {
            using var toFinal = await ChangeStatusAsync(id, status, "Fictional disposition note");
            Assert.Equal(HttpStatusCode.OK, toFinal.StatusCode);
        }
    }

    protected Task<HttpResponseMessage> SendRawJsonAsync(HttpMethod method, string route, string json)
    {
        var request = new HttpRequestMessage(method, route)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        return Client.SendAsync(request);
    }

    /// <summary>Reads a board page (200 expected) and returns its <c>data</c> (a PagedResult).</summary>
    protected async Task<JsonElement> GetBoardAsync(string query = "")
    {
        using var response = await Client.GetAsync(BoardRoute + query);
        return await AssertSuccessAsync(response, HttpStatusCode.OK);
    }

    protected static string[] Names(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("fullName").GetString() ?? "").ToArray();

    /// <summary>A JSON array of strings (for example <c>allowedTransitions</c>) as a plain array.</summary>
    protected static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(value => value.GetString() ?? "").ToArray();

    /// <summary>Success envelope: success true, no errors, no TraceId; returns <c>data</c>.</summary>
    protected static async Task<JsonElement> AssertSuccessAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"Expected {(int)expected}, got {(int)response.StatusCode}: {raw}");

        var body = Parse(response, raw);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Empty(body.GetProperty("errors").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("traceId").ValueKind);

        return body.GetProperty("data");
    }

    /// <summary>
    /// Failure envelope (api-response-contract.md): the expected status (never 200), success false, data null, a
    /// TraceId, optionally the exact message, and no internal details (stack trace, exception or type names, settings).
    /// </summary>
    protected static async Task<JsonElement> AssertFailureAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string? message = null)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"Expected {(int)expected}, got {(int)response.StatusCode}: {raw}");
        AssertNoInternals(raw);

        var body = Parse(response, raw);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));

        if (message is not null)
        {
            Assert.Equal(message, body.GetProperty("message").GetString());
        }

        return body;
    }

    /// <summary>The <c>field</c> of every error (a null field becomes an empty string).</summary>
    protected static string[] ErrorFields(JsonElement body) =>
        body.GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("field").GetString() ?? "").ToArray();

    protected static string[] ErrorMessages(JsonElement body) =>
        body.GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("message").GetString() ?? "").ToArray();

    protected static void AssertNoInternals(string raw)
    {
        Assert.DoesNotContain("   at ", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("stackTrace", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ERTriage.", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=", raw, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement Parse(HttpResponseMessage response, string raw)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }
}
