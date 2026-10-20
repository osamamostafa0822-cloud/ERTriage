using System.Net;
using ERTriage.Api.IntegrationTests.Persistence;

namespace ERTriage.Api.IntegrationTests.Http;

/// <summary>
/// <c>POST /api/patients/{id}/status</c> and <c>PUT /api/patients/{id}/priority-override</c>: valid transitions,
/// rejected transitions (409, nothing saved), mandatory reason/note rules (400) and unknown patients (404).
/// </summary>
public sealed class PatientWorkflowEndpointTests(ApiTestFixture fixture)
    : HttpTestBase(fixture), IClassFixture<ApiTestFixture>
{
    [SqlServerFact]
    public async Task ChangeStatus_WaitingToInTreatment_Returns200AndStampsTreatmentStart()
    {
        var id = await RegisterIdAsync();

        using var response = await ChangeStatusAsync(id, "InTreatment");
        var data = await AssertSuccessAsync(response, HttpStatusCode.OK);

        Assert.Equal("InTreatment", data.GetProperty("status").GetString());
        Assert.Equal(Now, data.GetProperty("treatmentStartedAt").GetDateTimeOffset());
        Assert.Equal(new[] { "Discharged", "Admitted" }, Strings(data.GetProperty("allowedTransitions")));
    }

    [SqlServerTheory]
    [InlineData("Discharged")]
    [InlineData("Admitted")]
    public async Task ChangeStatus_InTreatmentToFinal_WithNote_Returns200AndIsPersisted(string target)
    {
        var id = await RegisterIdAsync();
        await MoveToAsync(id, "InTreatment");

        using var response = await ChangeStatusAsync(id, target, "Fictional note");
        var data = await AssertSuccessAsync(response, HttpStatusCode.OK);

        Assert.Equal(target, data.GetProperty("status").GetString());
        Assert.Equal("Fictional note", data.GetProperty("dispositionNote").GetString());
        Assert.Equal(Now, data.GetProperty("completedAt").GetDateTimeOffset());
        Assert.Empty(data.GetProperty("allowedTransitions").EnumerateArray());

        using var reloaded = await GetPatientAsync(id);
        Assert.Equal(target, (await AssertSuccessAsync(reloaded, HttpStatusCode.OK)).GetProperty("status").GetString());
    }

    [SqlServerTheory]
    [InlineData("Waiting", "Discharged")]
    [InlineData("Waiting", "Admitted")]
    [InlineData("Waiting", "Waiting")]
    [InlineData("InTreatment", "Waiting")]
    [InlineData("InTreatment", "InTreatment")]
    [InlineData("Discharged", "InTreatment")]
    [InlineData("Admitted", "Discharged")]
    public async Task ChangeStatus_InvalidTransition_Returns409AndChangesNothing(string from, string to)
    {
        var id = await RegisterIdAsync();
        if (from != "Waiting")
        {
            await MoveToAsync(id, from);
        }

        // A note is sent only where the request itself is valid, so the 409 comes from the transition matrix.
        var note = to is "Discharged" or "Admitted" ? "Fictional note" : null;
        using var response = await ChangeStatusAsync(id, to, note);

        await AssertFailureAsync(response, HttpStatusCode.Conflict, $"Cannot change status from {from} to {to}.");

        using var reloaded = await GetPatientAsync(id);
        Assert.Equal(from, (await AssertSuccessAsync(reloaded, HttpStatusCode.OK)).GetProperty("status").GetString());
    }

    [SqlServerTheory]
    [InlineData("Discharged", null)]
    [InlineData("Admitted", "   ")]
    public async Task ChangeStatus_FinalStatusWithoutNote_Returns400ForDispositionNote(string target, string? note)
    {
        var id = await RegisterIdAsync();
        await MoveToAsync(id, "InTreatment");

        using var response = await ChangeStatusAsync(id, target, note);

        var body = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);
        Assert.Equal(new[] { "dispositionNote" }, ErrorFields(body));
    }

    [SqlServerFact]
    public async Task ChangeStatus_NoteWhenStartingTreatment_Returns400ForDispositionNote()
    {
        var id = await RegisterIdAsync();

        using var response = await ChangeStatusAsync(id, "InTreatment", "Not allowed here");

        var body = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);
        Assert.Equal(new[] { "dispositionNote" }, ErrorFields(body));
    }

    [SqlServerFact]
    public async Task ChangeStatus_MissingTargetStatus_Returns400ForTargetStatus()
    {
        var id = await RegisterIdAsync();

        using var response = await SendRawJsonAsync(HttpMethod.Post, $"{PatientsRoute}/{id}/status", "{}");

        var body = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);
        Assert.Equal(new[] { "targetStatus" }, ErrorFields(body));
    }

    [SqlServerFact]
    public async Task ChangeStatus_UnknownPatient_Returns404()
    {
        using var response = await ChangeStatusAsync(Guid.NewGuid(), "InTreatment");

        await AssertFailureAsync(response, HttpStatusCode.NotFound, PatientNotFound);
    }

    [SqlServerTheory]
    [InlineData("Waiting")]
    [InlineData("InTreatment")]
    public async Task Override_WithReason_Returns200AndKeepsCalculatedPriority(string status)
    {
        var id = await RegisterIdAsync(spO2: 98); // calculated priority 4
        if (status != "Waiting")
        {
            await MoveToAsync(id, status);
        }

        using var response = await OverrideAsync(id, 2, "Severe pain reported by nurse", "Nurse Fictional");
        var data = await AssertSuccessAsync(response, HttpStatusCode.OK);

        Assert.Equal(4, data.GetProperty("calculatedPriority").GetInt32());
        Assert.Equal(2, data.GetProperty("overridePriority").GetInt32());
        Assert.Equal(2, data.GetProperty("currentPriority").GetInt32());
        Assert.True(data.GetProperty("isOverridden").GetBoolean());
        Assert.Equal("Severe pain reported by nurse", data.GetProperty("overrideReason").GetString());
        Assert.Equal("Nurse Fictional", data.GetProperty("overriddenBy").GetString());
        Assert.Equal(Now, data.GetProperty("overriddenAt").GetDateTimeOffset());
        Assert.Equal(status, data.GetProperty("status").GetString());
    }

    [SqlServerTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")] // below the 3-character minimum
    public async Task Override_WithoutValidReason_Returns400AndChangesNothing(string? reason)
    {
        var id = await RegisterIdAsync(spO2: 98);

        using var response = await OverrideAsync(id, 1, reason);

        var body = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);
        Assert.Equal(new[] { "reason" }, ErrorFields(body));

        using var reloaded = await GetPatientAsync(id);
        var patient = await AssertSuccessAsync(reloaded, HttpStatusCode.OK);
        Assert.False(patient.GetProperty("isOverridden").GetBoolean());
        Assert.Equal(4, patient.GetProperty("currentPriority").GetInt32());
    }

    [SqlServerTheory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(null)]
    public async Task Override_PriorityOutOfRange_Returns400ForPriority(int? priority)
    {
        var id = await RegisterIdAsync();

        using var response = await OverrideAsync(id, priority, "Valid reason");

        var body = await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);
        Assert.Equal(new[] { "priority" }, ErrorFields(body));
    }

    [SqlServerTheory]
    [InlineData("Discharged")]
    [InlineData("Admitted")]
    public async Task Override_AfterCompletion_Returns409AndChangesNothing(string status)
    {
        var id = await RegisterIdAsync(spO2: 98);
        await MoveToAsync(id, status);

        using var response = await OverrideAsync(id, 1, "Too late for an override");

        var body = await AssertFailureAsync(response, HttpStatusCode.Conflict);
        Assert.Contains(status, body.GetProperty("message").GetString(), StringComparison.Ordinal);

        using var reloaded = await GetPatientAsync(id);
        Assert.False((await AssertSuccessAsync(reloaded, HttpStatusCode.OK)).GetProperty("isOverridden").GetBoolean());
    }

    [SqlServerFact]
    public async Task Override_UnknownPatient_Returns404()
    {
        using var response = await OverrideAsync(Guid.NewGuid(), 1, "Valid reason");

        await AssertFailureAsync(response, HttpStatusCode.NotFound, PatientNotFound);
    }

    [SqlServerFact]
    public async Task Override_InvalidBodyForUnknownPatient_Returns400BeforeLookup()
    {
        using var response = await OverrideAsync(Guid.NewGuid(), 1, reason: null);

        await AssertFailureAsync(response, HttpStatusCode.BadRequest, ValidationFailed);
    }
}
