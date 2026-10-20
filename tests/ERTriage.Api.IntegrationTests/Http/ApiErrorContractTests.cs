using System.Net;
using System.Text.RegularExpressions;
using ERTriage.Api.IntegrationTests.Persistence;
using ERTriage.Application.Common;
using ERTriage.Application.Triage;
using ERTriage.Application.Triage.Dtos;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ERTriage.Api.IntegrationTests.Http;

/// <summary>
/// The error contract across endpoints: binding errors (malformed JSON, wrong types, unknown enums) and unexpected
/// failures (500) all use <see cref="GeneralResponse{T}"/>, never leak internals or submitted values, and carry a
/// TraceId produced by the same rule as <c>GlobalExceptionHandler</c>. Also checks that patient text never reaches the logs.
/// </summary>
public sealed class ApiErrorContractTests(ApiTestFixture fixture)
    : HttpTestBase(fixture), IClassFixture<ApiTestFixture>
{
    private const string UnexpectedError = "An unexpected error occurred.";

    // W3C trace context sent by a caller; the API's activity becomes its child, so the TraceId must contain it.
    private const string CallerTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string CallerTraceParent = "00-" + CallerTraceId + "-00f067aa0ba902b7-01";

    private static readonly Regex W3CActivityId = new("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$");

    // Fixed id: theory data must be identical at discovery and execution. Binding fails before any lookup.
    private const string AnyId = "0192a5c4-7e00-7000-8000-000000000001";

    public static TheoryData<string, string> BodyEndpoints => new()
    {
        { "POST", PatientsRoute },
        { "POST", $"{PatientsRoute}/{AnyId}/status" },
        { "PUT", $"{PatientsRoute}/{AnyId}/priority-override" }
    };

    [SqlServerTheory]
    [MemberData(nameof(BodyEndpoints))]
    public async Task MalformedJson_Returns400EnvelopeWithoutEchoingTheInput(string method, string route)
    {
        const string secret = "Fictional-Secret-Value";

        using var response = await SendRawJsonAsync(new HttpMethod(method), route, "{\"fullName\":\"" + secret + "\",");

        var body = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);
        Assert.NotEmpty(ErrorFields(body));
        Assert.All(ErrorMessages(body), message => Assert.Equal(InvalidRequest, message));
        Assert.DoesNotContain(secret, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task Register_WrongJsonType_Returns400ForThatField()
    {
        const string secret = "forty-years-fictional";
        var json = "{\"fullName\":\"Test Patient\",\"age\":\"" + secret + "\"}";

        using var response = await SendRawJsonAsync(HttpMethod.Post, PatientsRoute, json);

        var body = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);
        Assert.Contains(ErrorFields(body), field => field.Contains("age", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(secret, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task ChangeStatus_UnknownEnumName_Returns400ForTargetStatus()
    {
        var id = await RegisterIdAsync();

        using var response = await SendRawJsonAsync(HttpMethod.Post, $"{PatientsRoute}/{id}/status", "{\"targetStatus\":\"Sleeping\"}");

        var body = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);
        Assert.Contains(ErrorFields(body), field => field.Contains("targetStatus", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Sleeping", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task ChangeStatus_UndefinedEnumNumber_Returns400ForTargetStatus()
    {
        var id = await RegisterIdAsync();

        using var response = await SendRawJsonAsync(HttpMethod.Post, $"{PatientsRoute}/{id}/status", "{\"targetStatus\":99}");

        var body = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);
        Assert.Equal(new[] { "targetStatus" }, ErrorFields(body));

        using var reloaded = await GetPatientAsync(id);
        Assert.Equal("Waiting", (await AssertSuccessAsync(reloaded, HttpStatusCode.OK)).GetProperty("status").GetString());
    }

    [SqlServerFact]
    public async Task UnexpectedFailure_Returns500WithGenericMessageAndNoInternals()
    {
        await using var failingApi = Fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITriageBoardService>();
                services.AddScoped<ITriageBoardService, ThrowingBoardService>();
            }));
        using var client = failingApi.CreateClient();

        using var response = await client.GetAsync(BoardRoute);

        var body = await AssertFailureAsync(response, HttpStatusCode.InternalServerError, UnexpectedError);
        Assert.Empty(body.GetProperty("errors").EnumerateArray());
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ThrowingBoardService.Detail, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ThrowingBoardService), raw, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task TraceId_UsesTheRequestActivityForHandlerAndBindingErrors()
    {
        // 404 from GlobalExceptionHandler and 400 from InvalidModelStateResponseFactory.
        var fromHandler = await SendWithTraceParentAsync($"{PatientsRoute}/{Guid.NewGuid()}");
        var fromBinding = await SendWithTraceParentAsync($"{PatientsRoute}/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, fromHandler.Status);
        Assert.Equal(HttpStatusCode.BadRequest, fromBinding.Status);

        foreach (var traceId in new[] { fromHandler.TraceId, fromBinding.TraceId })
        {
            Assert.Matches(W3CActivityId, traceId);
            Assert.StartsWith($"00-{CallerTraceId}-", traceId, StringComparison.Ordinal);
        }
    }

    [SqlServerFact]
    public async Task TraceId_IsDistinctPerRequestAndAbsentOnSuccess()
    {
        using var first = await GetPatientAsync(Guid.NewGuid());
        using var second = await GetPatientAsync("not-a-guid");
        var firstTrace = (await AssertFailureAsync(first, HttpStatusCode.NotFound)).GetProperty("traceId").GetString() ?? "";
        var secondTrace = (await AssertFailureAsync(second, HttpStatusCode.BadRequest)).GetProperty("traceId").GetString() ?? "";

        Assert.Matches(W3CActivityId, firstTrace);
        Assert.Matches(W3CActivityId, secondTrace);
        Assert.NotEqual(firstTrace, secondTrace);

        // Success responses carry no TraceId (AssertSuccessAsync checks it is null).
        await GetBoardAsync();
    }

    [SqlServerFact]
    public async Task PatientText_NeverAppearsInLogs()
    {
        const string name = "Zelda Fictional-Logcheck";
        const string complaint = "Logcheck fictional complaint";
        const string reason = "Logcheck fictional override reason";
        const string note = "Logcheck fictional disposition note";
        Fixture.Logs.Clear();

        var body = PatientBody(name);
        body["chiefComplaint"] = complaint;
        var id = (await RegisterAsync(body)).GetProperty("id").GetGuid();

        using (var overridden = await OverrideAsync(id, 2, reason))
        {
            await AssertSuccessAsync(overridden, HttpStatusCode.OK);
        }

        using (var invalid = await ChangeStatusAsync(id, "Discharged", note)) // 409: still Waiting
        {
            await AssertFailureAsync(invalid, HttpStatusCode.Conflict);
        }

        await MoveToAsync(id, "InTreatment");
        using (var discharged = await ChangeStatusAsync(id, "Discharged", note))
        {
            await AssertSuccessAsync(discharged, HttpStatusCode.OK);
        }

        using (var rejected = await SendRawJsonAsync(HttpMethod.Post, PatientsRoute, "{\"fullName\":\"" + name + "\","))
        {
            await AssertFailureAsync(rejected, HttpStatusCode.BadRequest);
        }

        var entries = Fixture.Logs.Entries;
        Assert.Contains(entries, entry => entry.Message.Contains("Patient registered", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Message.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase));

        foreach (var text in new[] { name, complaint, reason, note })
        {
            Assert.DoesNotContain(entries, entry => entry.Message.Contains(text, StringComparison.OrdinalIgnoreCase));
        }
    }

    private async Task<(HttpStatusCode Status, string TraceId)> SendWithTraceParentAsync(string route)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.TryAddWithoutValidation("traceparent", CallerTraceParent);

        using var response = await Client.SendAsync(request);
        var body = await AssertFailureAsync(response, response.StatusCode);

        return (response.StatusCode, body.GetProperty("traceId").GetString() ?? "");
    }

    private sealed class ThrowingBoardService : ITriageBoardService
    {
        public const string Detail = "Fictional internal detail: Server=db-host;Password=not-a-real-secret";

        public Task<PagedResult<TriageBoardItemDto>> GetBoardAsync(TriageBoardRequest? request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Detail);
    }
}
