using System.Text.Json.Nodes;
using ERTriage.Domain.Patients;

namespace ERTriage.Api.IntegrationTests.Seed;

/// <summary>Expected facts about the shipped seed file and helpers to build variations of it in tests.</summary>
internal static class SeedTestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Calculated priority per key, produced by the Domain from the vitals in the file (D-58 table).</summary>
    public static readonly IReadOnlyDictionary<string, int> CalculatedPriority = new Dictionary<string, int>
    {
        ["alpha"] = 1, ["bravo"] = 1, ["charlie"] = 2, ["delta"] = 2, ["echo"] = 2, ["foxtrot"] = 3,
        ["golf"] = 3, ["hotel"] = 3, ["india"] = 4, ["juliet"] = 4, ["kilo"] = 3, ["lima"] = 1
    };

    public static readonly IReadOnlyDictionary<string, PatientStatus> Status = new Dictionary<string, PatientStatus>
    {
        ["alpha"] = PatientStatus.Waiting, ["bravo"] = PatientStatus.InTreatment, ["charlie"] = PatientStatus.Waiting,
        ["delta"] = PatientStatus.Waiting, ["echo"] = PatientStatus.InTreatment, ["foxtrot"] = PatientStatus.Waiting,
        ["golf"] = PatientStatus.Waiting, ["hotel"] = PatientStatus.Waiting, ["india"] = PatientStatus.Waiting,
        ["juliet"] = PatientStatus.Waiting, ["kilo"] = PatientStatus.Discharged, ["lima"] = PatientStatus.Admitted
    };

    /// <summary>Approved default board (Waiting + InTreatment) on a database holding only the seed.</summary>
    public static readonly string[] DefaultBoardOrder =
    [
        "Test Patient Bravo", "Test Patient Alpha", "Test Patient Echo", "Test Patient Delta", "Test Patient Charlie",
        "Test Patient India", "Test Patient Hotel", "Test Patient Foxtrot", "Test Patient Golf", "Test Patient Juliet"
    ];

    public static readonly string[] CompletedBoardOrder = ["Test Patient Lima", "Test Patient Kilo"];

    public static string ShippedFilePath =>
        Path.Combine(AppContext.BaseDirectory, "Persistence", "Seed", "Data", "fictional-patients.json");

    /// <summary>A valid Waiting patient record as a mutable JSON object.</summary>
    public static JsonObject WaitingPatient(string key = "one") => new()
    {
        ["seedKey"] = key,
        ["fullName"] = "Test Patient One",
        ["age"] = 40,
        ["chiefComplaint"] = "Chest pain",
        ["vitals"] = new JsonObject
        {
            ["heartRate"] = 80, ["systolicBp"] = 120, ["diastolicBp"] = 80, ["temperatureC"] = 36.8m, ["spO2"] = 98
        },
        ["arrivedMinutesAgo"] = 30,
        ["status"] = "Waiting"
    };

    /// <summary>A valid Discharged patient (treatment at +10, completed at +20, arrived 60 min ago).</summary>
    public static JsonObject DischargedPatient(string key = "done")
    {
        var patient = WaitingPatient(key);
        patient["arrivedMinutesAgo"] = 60;
        patient["status"] = "Discharged";
        patient["treatmentStartedMinutesAfterArrival"] = 10;
        patient["completedMinutesAfterArrival"] = 20;
        patient["dispositionNote"] = "Fictional note";
        return patient;
    }

    public static string File(params JsonObject[] patients) =>
        new JsonObject { ["schemaVersion"] = 1, ["patients"] = new JsonArray(patients.Cast<JsonNode?>().ToArray()) }.ToJsonString();
}
