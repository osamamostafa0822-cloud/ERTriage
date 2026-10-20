using System.Net;
using System.Text.Json;
using ERTriage.Api.IntegrationTests.Persistence;

namespace ERTriage.Api.IntegrationTests.Http;

/// <summary>
/// <c>GET /api/triage-board</c>: default statuses, business ordering (priority, then arrival), overrides, repeated
/// filters, waiting-time indicator (fixed clock) and pagination boundaries.
/// </summary>
public sealed class TriageBoardEndpointTests(ApiTestFixture fixture)
    : HttpTestBase(fixture), IClassFixture<ApiTestFixture>
{
    [SqlServerFact]
    public async Task Board_WithoutStatusFilter_ShowsWaitingAndInTreatmentOnly()
    {
        await RegisterIdAsync("Waiting One", arrival: Now.AddMinutes(-40));
        var treated = await RegisterIdAsync("Treated One", arrival: Now.AddMinutes(-30));
        var discharged = await RegisterIdAsync("Discharged One", arrival: Now.AddMinutes(-20));
        var admitted = await RegisterIdAsync("Admitted One", arrival: Now.AddMinutes(-10));
        await MoveToAsync(treated, "InTreatment");
        await MoveToAsync(discharged, "Discharged");
        await MoveToAsync(admitted, "Admitted");

        var page = await GetBoardAsync();

        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { "Waiting One", "Treated One" }, Names(page));
    }

    [SqlServerFact]
    public async Task Board_OrdersByPriorityThenArrivalTime()
    {
        await RegisterIdAsync("P4 earliest", spO2: 98, arrival: Now.AddMinutes(-90));
        await RegisterIdAsync("P2 later", spO2: 92, arrival: Now.AddMinutes(-10));
        await RegisterIdAsync("P1 latest", spO2: 85, arrival: Now.AddMinutes(-1));
        await RegisterIdAsync("P2 earlier", spO2: 92, arrival: Now.AddMinutes(-50));
        await RegisterIdAsync("P3", spO2: 95, arrival: Now.AddMinutes(-60));

        var page = await GetBoardAsync();

        Assert.Equal(new[] { "P1 latest", "P2 earlier", "P2 later", "P3", "P4 earliest" }, Names(page));
        Assert.Equal(
            new[] { 1, 2, 2, 3, 4 },
            page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("currentPriority").GetInt32()));
    }

    [SqlServerFact]
    public async Task Board_OverrideReordersThePatientAndIsShownOnTheRow()
    {
        await RegisterIdAsync("Calculated P1", spO2: 85, arrival: Now.AddMinutes(-5));
        var overridden = await RegisterIdAsync("Calculated P4", spO2: 98, arrival: Now.AddMinutes(-30));

        using (var response = await OverrideAsync(overridden, 1, "Deteriorating, nurse judgement"))
        {
            await AssertSuccessAsync(response, HttpStatusCode.OK);
        }

        var page = await GetBoardAsync();
        var first = page.GetProperty("items")[0];

        // Both are now priority 1; the overridden patient arrived first.
        Assert.Equal(new[] { "Calculated P4", "Calculated P1" }, Names(page));
        Assert.Equal(1, first.GetProperty("currentPriority").GetInt32());
        Assert.Equal(4, first.GetProperty("calculatedPriority").GetInt32());
        Assert.True(first.GetProperty("isOverridden").GetBoolean());
        Assert.Equal("Deteriorating, nurse judgement", first.GetProperty("overrideReason").GetString());
    }

    [SqlServerFact]
    public async Task Board_RepeatedPriorityFilter_ReturnsOnlyThosePriorities()
    {
        await RegisterIdAsync("P1", spO2: 85);
        await RegisterIdAsync("P2", spO2: 92);
        await RegisterIdAsync("P3", spO2: 95);
        await RegisterIdAsync("P4", spO2: 98);

        var page = await GetBoardAsync("?priority=1&priority=3");

        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { "P1", "P3" }, Names(page));
    }

    [SqlServerFact]
    public async Task Board_PriorityFilterUsesTheCurrentPriority()
    {
        var overridden = await RegisterIdAsync("Calculated P4", spO2: 98);
        await RegisterIdAsync("Plain P4", spO2: 98);
        using (var response = await OverrideAsync(overridden, 2, "Nurse judgement"))
        {
            await AssertSuccessAsync(response, HttpStatusCode.OK);
        }

        Assert.Equal(new[] { "Calculated P4" }, Names(await GetBoardAsync("?priority=2")));
        Assert.Equal(new[] { "Plain P4" }, Names(await GetBoardAsync("?priority=4")));
    }

    [SqlServerFact]
    public async Task Board_RepeatedStatusFilter_OverridesTheDefault()
    {
        await RegisterIdAsync("Waiting One");
        var discharged = await RegisterIdAsync("Discharged One", arrival: Now.AddMinutes(-20));
        var admitted = await RegisterIdAsync("Admitted One", arrival: Now.AddMinutes(-10));
        await MoveToAsync(discharged, "Discharged");
        await MoveToAsync(admitted, "Admitted");

        var page = await GetBoardAsync("?status=Discharged&status=Admitted");

        Assert.Equal(new[] { "Discharged One", "Admitted One" }, Names(page));
        foreach (var item in page.GetProperty("items").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Null, item.GetProperty("waitingMinutes").ValueKind);
            Assert.Equal("None", item.GetProperty("waitingLevel").GetString());
            Assert.Empty(item.GetProperty("allowedTransitions").EnumerateArray());
        }
    }

    [SqlServerFact]
    public async Task Board_StatusAndPriorityFiltersCombine()
    {
        await RegisterIdAsync("Waiting P1", spO2: 85);
        await RegisterIdAsync("Waiting P4", spO2: 98);
        var treated = await RegisterIdAsync("Treated P1", spO2: 85);
        await MoveToAsync(treated, "InTreatment");

        var page = await GetBoardAsync("?status=Waiting&priority=1");

        Assert.Equal(new[] { "Waiting P1" }, Names(page));
    }

    [SqlServerFact]
    public async Task Board_WaitingTimeUsesTheFixedClock()
    {
        await RegisterIdAsync("Waited 60", arrival: Now.AddMinutes(-60));
        await RegisterIdAsync("Waited 59", arrival: Now.AddMinutes(-59));
        await RegisterIdAsync("Waited 30", arrival: Now.AddMinutes(-30));
        await RegisterIdAsync("Waited 29", arrival: Now.AddMinutes(-29));
        await RegisterIdAsync("Arrives in 5", arrival: Now.AddMinutes(5));
        var treated = await RegisterIdAsync("Treated after 40", arrival: Now.AddMinutes(-40));
        await MoveToAsync(treated, "InTreatment"); // treatment starts at Now: waiting time frozen at 40

        var items = (await GetBoardAsync()).GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("fullName").GetString() ?? "");

        AssertWaiting(items["Waited 60"], 60, "Critical");
        AssertWaiting(items["Waited 59"], 59, "Warning");
        AssertWaiting(items["Waited 30"], 30, "Warning");
        AssertWaiting(items["Waited 29"], 29, "Normal");
        AssertWaiting(items["Arrives in 5"], 0, "Normal");
        AssertWaiting(items["Treated after 40"], 40, "None");
    }

    [SqlServerFact]
    public async Task Board_Pagination_DefaultSizeLastPageAndBeyondTheEnd()
    {
        for (var i = 0; i < 12; i++)
        {
            // Distinct arrivals give a fully determined order: Patient 00 arrived first.
            await RegisterIdAsync($"Patient {i:00}", arrival: Now.AddMinutes(-60 + i));
        }

        var first = await GetBoardAsync();
        AssertPage(first, totalCount: 12, pageNumber: 1, pageSize: 10, itemCount: 10);
        Assert.Equal("Patient 00", Names(first)[0]);

        var last = await GetBoardAsync("?pageNumber=2");
        AssertPage(last, totalCount: 12, pageNumber: 2, pageSize: 10, itemCount: 2);
        Assert.Equal(new[] { "Patient 10", "Patient 11" }, Names(last));

        var beyond = await GetBoardAsync("?pageNumber=3");
        AssertPage(beyond, totalCount: 12, pageNumber: 3, pageSize: 10, itemCount: 0);

        var single = await GetBoardAsync("?pageSize=1&pageNumber=12");
        AssertPage(single, totalCount: 12, pageNumber: 12, pageSize: 1, itemCount: 1);
        Assert.Equal(new[] { "Patient 11" }, Names(single));

        var maximum = await GetBoardAsync("?pageSize=100");
        AssertPage(maximum, totalCount: 12, pageNumber: 1, pageSize: 100, itemCount: 12);
    }

    [SqlServerTheory]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=101", "pageSize")]
    [InlineData("?pageNumber=0", "pageNumber")]
    [InlineData("?pageNumber=-1", "pageNumber")]
    [InlineData("?priority=0", "priority")]
    [InlineData("?priority=5", "priority")]
    [InlineData("?priority=1&priority=9", "priority")]
    public async Task Board_OutOfRangeQuery_Returns400ForThatParameter(string query, string field)
    {
        using var response = await Client.GetAsync(BoardRoute + query);

        var body = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);
        Assert.Equal(new[] { field }, ErrorFields(body));
    }

    [SqlServerFact]
    public async Task Board_UndefinedNumericStatus_Returns400ForStatus()
    {
        // Rejected either by model binding or by the board validator; both report the "status" parameter.
        using var response = await Client.GetAsync(BoardRoute + "?status=99");

        var body = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);
        Assert.Contains("status", ErrorFields(body));
    }

    [SqlServerTheory]
    [InlineData("?status=Sleeping", "status")]
    [InlineData("?priority=high", "priority")]
    [InlineData("?pageSize=ten", "pageSize")]
    [InlineData("?pageNumber=1.5", "pageNumber")]
    public async Task Board_UnparsableQuery_Returns400ForThatParameter(string query, string field)
    {
        using var response = await Client.GetAsync(BoardRoute + query);

        var body = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);
        Assert.Contains(field, ErrorFields(body));
        Assert.All(ErrorMessages(body), message => Assert.Equal(InvalidRequest, message));
    }

    private static void AssertWaiting(JsonElement item, int minutes, string level)
    {
        Assert.Equal(minutes, item.GetProperty("waitingMinutes").GetInt32());
        Assert.Equal(level, item.GetProperty("waitingLevel").GetString());
    }

    private static void AssertPage(JsonElement page, int totalCount, int pageNumber, int pageSize, int itemCount)
    {
        Assert.Equal(totalCount, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(pageNumber, page.GetProperty("pageNumber").GetInt32());
        Assert.Equal(pageSize, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(itemCount, page.GetProperty("items").GetArrayLength());
    }
}
