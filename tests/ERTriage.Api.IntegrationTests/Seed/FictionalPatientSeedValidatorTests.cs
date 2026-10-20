using System.Text.Json.Nodes;
using ERTriage.Infrastructure.Persistence.Seed;

namespace ERTriage.Api.IntegrationTests.Seed;

/// <summary>
/// Validation of the seed JSON (D-60, D-62): each record is validated on its own; an invalid record is skipped with
/// its index, safe seed key, error code and field, and the valid records are kept. Unusable documents are rejected.
/// </summary>
public class FictionalPatientSeedValidatorTests
{
    [Fact]
    public void ValidFile_HasNoIssues()
    {
        var result = FictionalPatientSeedLoader.Parse(SeedTestData.File(SeedTestData.WaitingPatient(), SeedTestData.DischargedPatient()));

        Assert.Equal(2, result.TotalRecords);
        Assert.Equal(2, result.Valid.Count);
        Assert.Empty(result.Issues);
        Assert.Equal([0, 1], result.Valid.Select(v => v.Index));
    }

    [Theory]
    [InlineData("seedKey", null, "Required")]
    [InlineData("seedKey", "\"Alpha\"", "InvalidFormat")]
    [InlineData("seedKey", "\"has space\"", "InvalidFormat")]
    [InlineData("seedKey", "\"-leading\"", "InvalidFormat")]
    [InlineData("fullName", null, "Required")]
    [InlineData("fullName", "\"John Smith\"", "NotFictionalName")]
    [InlineData("age", null, "Required")]
    [InlineData("age", "121", "OutOfRange")]
    [InlineData("age", "-1", "OutOfRange")]
    [InlineData("chiefComplaint", "\"x\"", "InvalidLength")]
    [InlineData("vitals", null, "Required")]
    [InlineData("vitals.heartRate", "19", "OutOfRange")]
    [InlineData("vitals.systolicBp", "301", "OutOfRange")]
    [InlineData("vitals.diastolicBp", "120", "DiastolicNotBelowSystolic")]
    [InlineData("vitals.temperatureC", "37.25", "PrecisionExceeded")]
    [InlineData("vitals.temperatureC", "45.1", "OutOfRange")]
    [InlineData("vitals.spO2", "101", "OutOfRange")]
    [InlineData("arrivedMinutesAgo", null, "Required")]
    [InlineData("arrivedMinutesAgo", "-1", "OutOfRange")]
    [InlineData("arrivedMinutesAgo", "1441", "OutOfRange")]
    [InlineData("status", null, "Required")]
    [InlineData("status", "\"Sleeping\"", "InvalidFormat")]
    [InlineData("status", "\"waiting\"", "InvalidFormat")]
    [InlineData("status", "\"1\"", "InvalidFormat")]
    [InlineData("treatmentStartedMinutesAfterArrival", "5", "NotAllowedForStatus")]
    [InlineData("completedMinutesAfterArrival", "5", "NotAllowedForStatus")]
    [InlineData("dispositionNote", "\"note\"", "NotAllowedForStatus")]
    public void InvalidField_SkipsOnlyThatRecord_WithCodeAndField(string field, string? jsonValue, string expectedCode)
    {
        var bad = SeedTestData.WaitingPatient("bad");
        Set(bad, field, jsonValue);

        var result = FictionalPatientSeedLoader.Parse(SeedTestData.File(SeedTestData.WaitingPatient("good"), bad));

        var issue = Assert.Single(result.Issues);
        Assert.Equal((1, expectedCode, field), (issue.Index, issue.Code, issue.Field));
        Assert.Equal(field == "seedKey" ? null : "bad", issue.SeedKey);
        Assert.Equal(["good"], result.Valid.Select(v => v.SeedKey));
        Assert.Equal(1, result.SkippedCount);
    }

    [Theory]
    [InlineData("treatmentStartedMinutesAfterArrival", null, "RequiredForStatus", "treatmentStartedMinutesAfterArrival")]
    [InlineData("completedMinutesAfterArrival", null, "RequiredForStatus", "completedMinutesAfterArrival")]
    [InlineData("dispositionNote", null, "RequiredForStatus", "dispositionNote")]
    [InlineData("dispositionNote", "\"   \"", "RequiredForStatus", "dispositionNote")]
    [InlineData("completedMinutesAfterArrival", "5", "TimelineOrder", "completedMinutesAfterArrival")]
    [InlineData("treatmentStartedMinutesAfterArrival", "61", "InFuture", "treatmentStartedMinutesAfterArrival")]
    [InlineData("completedMinutesAfterArrival", "61", "InFuture", "completedMinutesAfterArrival")]
    [InlineData("treatmentStartedMinutesAfterArrival", "-1", "InFuture", "treatmentStartedMinutesAfterArrival")]
    public void DischargedRecord_InvalidTimeline_IsSkipped(string field, string? jsonValue, string expectedCode, string expectedField)
    {
        var patient = SeedTestData.DischargedPatient();
        Set(patient, field, jsonValue);

        var result = FictionalPatientSeedLoader.Parse(SeedTestData.File(patient));

        Assert.Contains(result.Issues, i => i.Code == expectedCode && i.Field == expectedField);
        Assert.Empty(result.Valid);
    }

    [Fact]
    public void InTreatmentRecord_WithoutTreatmentTime_IsSkipped()
    {
        var patient = SeedTestData.WaitingPatient();
        patient["status"] = "InTreatment";

        var issue = Assert.Single(FictionalPatientSeedLoader.Parse(SeedTestData.File(patient)).Issues);

        Assert.Equal(("RequiredForStatus", "treatmentStartedMinutesAfterArrival"), (issue.Code, issue.Field));
    }

    [Theory]
    [InlineData("reason", null, "Required")]
    [InlineData("reason", "\"   \"", "Required")]
    [InlineData("reason", "\"ab\"", "InvalidLength")]
    [InlineData("priority", "5", "OutOfRange")]
    [InlineData("priority", "0", "OutOfRange")]
    [InlineData("priority", null, "Required")]
    [InlineData("minutesAfterArrival", null, "Required")]
    [InlineData("minutesAfterArrival", "61", "InFuture")]
    [InlineData("minutesAfterArrival", "21", "OverrideAfterCompletion")]
    public void InvalidOverride_IsSkipped(string field, string? jsonValue, string expectedCode)
    {
        var patient = SeedTestData.DischargedPatient();
        var priorityOverride = new JsonObject { ["priority"] = 2, ["reason"] = "Valid reason", ["minutesAfterArrival"] = 5 };
        Set(priorityOverride, field, jsonValue);
        patient["priorityOverride"] = priorityOverride;

        var result = FictionalPatientSeedLoader.Parse(SeedTestData.File(patient));

        Assert.Contains(result.Issues, i => i.Code == expectedCode && i.Field == $"priorityOverride.{field}");
        Assert.Empty(result.Valid);
    }

    [Fact]
    public void Override_AtCompletionMinute_IsAppliedBeforeTheStatusChange()
    {
        var patient = SeedTestData.DischargedPatient();
        patient["priorityOverride"] = new JsonObject { ["priority"] = 1, ["reason"] = "Valid reason", ["minutesAfterArrival"] = 20 };

        var seed = Assert.Single(FictionalPatientSeedLoader.Parse(SeedTestData.File(patient)).Valid);
        var created = FictionalPatientFactory.Create(seed, SeedTestData.Now);

        Assert.Equal(1, created.CurrentPriority);
        Assert.Equal(ERTriage.Domain.Patients.PatientStatus.Discharged, created.Status);
    }

    [Fact]
    public void DuplicateSeedKey_SkipsEveryRecordSharingIt_Deterministically()
    {
        var result = FictionalPatientSeedLoader.Parse(SeedTestData.File(
            SeedTestData.WaitingPatient("same"),
            SeedTestData.WaitingPatient("unique"),
            SeedTestData.WaitingPatient("same")));

        Assert.Equal(["unique"], result.Valid.Select(v => v.SeedKey));
        Assert.Equal(
            [(0, "same", "DuplicateSeedKey"), (2, "same", "DuplicateSeedKey")],
            result.Issues.Select(i => (i.Index, i.SeedKey, i.Code)));
    }

    [Fact]
    public void SeveralInvalidRecords_AreAllSkipped_AndTheValidOnesKept()
    {
        var first = SeedTestData.WaitingPatient("first");
        first["age"] = 500;
        var second = SeedTestData.WaitingPatient("second");
        second["status"] = "Unknown";

        var result = FictionalPatientSeedLoader.Parse(SeedTestData.File(first, SeedTestData.WaitingPatient("ok"), second));

        Assert.Equal(["ok"], result.Valid.Select(v => v.SeedKey));
        Assert.Equal(2, result.SkippedCount);
        Assert.Contains(result.Issues, i => (i.Index, i.SeedKey, i.Code, i.Field) == (0, "first", "OutOfRange", "age"));
        Assert.Contains(result.Issues, i => (i.Index, i.SeedKey, i.Code, i.Field) == (2, "second", "InvalidFormat", "status"));
    }

    [Theory]
    [InlineData("{\"seedKey\":\"typo\",\"unknownField\":1}", "InvalidJson", "unknownField", "typo")]
    [InlineData("{\"seedKey\":\"str\",\"age\":\"40\"}", "InvalidJson", "age", "str")]
    [InlineData("{\"seedKey\":\"BAD KEY\",\"age\":\"40\"}", "InvalidJson", "age", null)]
    [InlineData("null", "NullRecord", "$", null)]
    public void UnreadableRecord_IsSkipped_OthersKept(string recordJson, string expectedCode, string expectedField, string? expectedKey)
    {
        var json = "{\"schemaVersion\":1,\"patients\":[" + SeedTestData.WaitingPatient("good").ToJsonString() + "," + recordJson + "]}";

        var result = FictionalPatientSeedLoader.Parse(json);

        var issue = Assert.Single(result.Issues);
        Assert.Equal((1, expectedCode, expectedField, expectedKey), (issue.Index, issue.Code, issue.Field, issue.SeedKey));
        Assert.Equal(["good"], result.Valid.Select(v => v.SeedKey));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"patients\":[]}", "UnsupportedSchemaVersion")]
    [InlineData("{\"schemaVersion\":1,\"patients\":[]}", "NoRecords")]
    [InlineData("{\"schemaVersion\":1}", "NoRecords")]
    [InlineData("null", "EmptyDocument")]
    [InlineData("{\"schemaVersion\":1,\"patients\":[", "MalformedDocument")]
    [InlineData("{\"schemaVersion\":1,\"patients\":{}}", "MalformedDocument")]
    [InlineData("{\"schemaVersion\":1,\"patients\":[],\"extra\":true}", "MalformedDocument")]
    public void UnusableDocument_IsRejectedAsAWhole(string json, string expectedCode)
    {
        var ex = Assert.Throws<SeedDataException>(() => FictionalPatientSeedLoader.Parse(json));

        Assert.Equal(expectedCode, ex.Code);
    }

    [Fact]
    public async Task MissingFile_IsRejectedWithoutExposingThePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");

        var ex = await Assert.ThrowsAsync<SeedDataException>(() => FictionalPatientSeedLoader.LoadAsync(path, CancellationToken.None));

        Assert.Equal("FileNotFound", ex.Code);
        Assert.DoesNotContain(Path.GetTempPath(), ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Sets (or removes, for a null value) a property; supports one level of nesting ("vitals.heartRate").</summary>
    private static void Set(JsonObject target, string path, string? jsonValue)
    {
        var parts = path.Split('.');
        var owner = parts.Length == 2 ? (JsonObject)target[parts[0]]! : target;
        var name = parts[^1];

        if (jsonValue is null)
        {
            owner.Remove(name);
        }
        else
        {
            owner[name] = JsonNode.Parse(jsonValue);
        }
    }
}
