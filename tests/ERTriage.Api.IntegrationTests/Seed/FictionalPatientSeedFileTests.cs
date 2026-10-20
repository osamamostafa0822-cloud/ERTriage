using ERTriage.Application.Triage;
using ERTriage.Domain.Patients;
using ERTriage.Infrastructure.Persistence.Seed;

namespace ERTriage.Api.IntegrationTests.Seed;

/// <summary>The shipped JSON file: present in the output, valid, and producing the approved demo through the Domain.</summary>
public class FictionalPatientSeedFileTests
{
    [Fact]
    public void ShippedFile_IsCopiedToTheBuildOutput()
    {
        Assert.True(File.Exists(SeedTestData.ShippedFilePath));
        Assert.Equal(SeedTestData.ShippedFilePath, FictionalPatientSeedLoader.DefaultPath);
    }

    [Fact]
    public async Task ShippedFile_LoadsTwelveFictionalPatientsWithUniqueStableKeys()
    {
        var result = await FictionalPatientSeedLoader.LoadAsync(SeedTestData.ShippedFilePath, CancellationToken.None);
        var patients = result.Valid;

        Assert.Empty(result.Issues);
        Assert.Equal(12, result.TotalRecords);
        Assert.Equal(12, patients.Count);
        Assert.Equal(SeedTestData.CalculatedPriority.Keys.Order(), patients.Select(p => p.SeedKey).Order());
        Assert.All(patients, p => Assert.StartsWith("Test Patient ", p.FullName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Factory_ProducesTheApprovedPrioritiesStatusesAndOverrideThroughTheDomain()
    {
        var patients = await CreateAllAsync();

        foreach (var (key, patient) in patients)
        {
            Assert.Equal(SeedTestData.CalculatedPriority[key], patient.CalculatedPriority);
            Assert.Equal(SeedTestData.Status[key], patient.Status);
            Assert.Equal(patient.OverridePriority ?? patient.CalculatedPriority, patient.CurrentPriority);
        }

        var overridden = Assert.Single(patients.Values, p => p.IsOverridden);
        Assert.Equal("Test Patient India", overridden.FullName);
        Assert.Equal(4, overridden.CalculatedPriority);
        Assert.Equal(2, overridden.CurrentPriority);
        Assert.Equal("Pain escalating on reassessment", overridden.OverrideReason);

        Assert.Equal("Treated and discharged with follow-up advice.", patients["kilo"].DispositionNote);
        Assert.Equal("Admitted to the medical ward for observation.", patients["lima"].DispositionNote);
        Assert.Equal(8, patients.Values.Count(p => p.Status == PatientStatus.Waiting));
        Assert.Equal(2, patients.Values.Count(p => p.Status == PatientStatus.InTreatment));
    }

    [Fact]
    public async Task Factory_TimestampsAreOrderedAndNeverInTheFuture()
    {
        var now = SeedTestData.Now.UtcDateTime;
        var patients = await CreateAllAsync();

        foreach (var patient in patients.Values)
        {
            Assert.True(patient.ArrivalTime <= now);
            Assert.True(patient.ArrivalTime >= now.AddHours(-2));
            Assert.Equal(DateTimeKind.Utc, patient.ArrivalTime.Kind);

            if (patient.TreatmentStartedAt is { } started)
            {
                Assert.InRange(started, patient.ArrivalTime, now);
            }

            if (patient.CompletedAt is { } completed)
            {
                Assert.InRange(completed, patient.TreatmentStartedAt!.Value, now);
            }

            if (patient.OverriddenAt is { } overriddenAt)
            {
                Assert.InRange(overriddenAt, patient.ArrivalTime, now);
            }
        }

        Assert.Equal(now.AddMinutes(-40 + 10), patients["bravo"].TreatmentStartedAt);
        Assert.Equal(now.AddMinutes(-120 + 60), patients["kilo"].CompletedAt);
        Assert.Equal(now.AddMinutes(-25 + 5), patients["india"].OverriddenAt);
    }

    [Fact]
    public async Task Factory_DefaultBoardOrderAndWaitingLevelsMatchTheApprovedDemo()
    {
        var patients = await CreateAllAsync();

        // Same ordering as PatientRepository.GetBoardPageAsync: CurrentPriority, ArrivalTime, Id.
        var board = patients.Values
            .Where(p => p.Status is PatientStatus.Waiting or PatientStatus.InTreatment)
            .OrderBy(p => p.CurrentPriority).ThenBy(p => p.ArrivalTime).ThenBy(p => p.Id)
            .Select(p => p.FullName)
            .ToArray();

        Assert.Equal(SeedTestData.DefaultBoardOrder, board);

        WaitingLevel Level(string key) => WaitingTimeEvaluator.Evaluate(patients[key], SeedTestData.Now).Level;
        Assert.Equal(WaitingLevel.Normal, Level("alpha"));
        Assert.Equal(WaitingLevel.Warning, Level("charlie"));
        Assert.Equal(WaitingLevel.Critical, Level("hotel"));
        Assert.Equal(WaitingLevel.None, Level("bravo"));
        Assert.Equal(10, WaitingTimeEvaluator.Evaluate(patients["bravo"], SeedTestData.Now).Minutes);
    }

    private static async Task<Dictionary<string, Patient>> CreateAllAsync()
    {
        var seeds = (await FictionalPatientSeedLoader.LoadAsync(SeedTestData.ShippedFilePath, CancellationToken.None)).Valid;
        return seeds.ToDictionary(s => s.SeedKey, s => FictionalPatientFactory.Create(s, SeedTestData.Now));
    }
}
