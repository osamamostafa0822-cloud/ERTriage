using ERTriage.Domain.Patients;
using ERTriage.Infrastructure.Persistence.Seed;
using ERTriage.Application.Triage.Waiting;

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
    public async Task ShippedFile_Holds5012ValidFictionalRecords_TheTwelveCuratedFirst()
    {
        var result = await FictionalPatientSeedLoader.LoadAsync(SeedTestData.ShippedFilePath, CancellationToken.None);

        Assert.Empty(result.Issues);
        Assert.Equal(5012, result.TotalRecords);
        Assert.Equal(5012, result.Valid.Count);
        Assert.Equal(SeedTestData.CuratedKeys.Order(), result.Valid.Take(12).Select(p => p.SeedKey).Order());
        Assert.Equal(
            Enumerable.Range(1, 5000).Select(n => $"vol-{n:00000}"),
            result.Valid.Skip(12).Select(p => p.SeedKey));
        Assert.All(result.Valid, p => Assert.StartsWith("Test Patient ", p.FullName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CuratedFile_HoldsExactlyTheTwelveDemoPatients()
    {
        var result = await FictionalPatientSeedLoader.LoadAsync(SeedTestData.CuratedFilePath, CancellationToken.None);

        Assert.Empty(result.Issues);
        Assert.Equal(SeedTestData.CuratedKeys.Order(), result.Valid.Select(p => p.SeedKey).Order());
    }

    [Fact]
    public async Task GeneratedRows_CoverEveryPriorityAndStatus_WithOrderedTimestamps()
    {
        var seeds = (await FictionalPatientSeedLoader.LoadAsync(SeedTestData.ShippedFilePath, CancellationToken.None)).Valid.Skip(12);
        var now = SeedTestData.Now.UtcDateTime;
        var patients = seeds.Select(s => FictionalPatientFactory.Create(s, SeedTestData.Now)).ToList();

        Assert.Equal(5000, patients.Count);
        Assert.Equal([1, 2, 3, 4], patients.Select(p => p.CalculatedPriority).Distinct().Order());
        Assert.Equal(4, patients.Select(p => p.Status).Distinct().Count());
        Assert.Contains(patients, p => p.IsOverridden);
        Assert.Equal(5000, patients.Select(p => p.Id).Distinct().Count());
        Assert.All(patients, p =>
        {
            Assert.InRange(p.ArrivalTime, now.AddHours(-24), now);
            if (p.TreatmentStartedAt is { } started) Assert.InRange(started, p.ArrivalTime, now);
            if (p.CompletedAt is { } completed) Assert.InRange(completed, p.TreatmentStartedAt!.Value, now);
            if (p.OverriddenAt is { } overridden) Assert.InRange(overridden, p.ArrivalTime, p.CompletedAt ?? now);
        });
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

        // Same ordering as TriageBoardReadRepository.GetPageAsync: CurrentPriority, ArrivalTime, Id.
        var board = patients.Values
            .Where(p => p.Status is PatientStatus.Waiting or PatientStatus.InTreatment)
            .OrderBy(p => p.CurrentPriority).ThenBy(p => p.ArrivalTime).ThenBy(p => p.Id)
            .Select(p => p.FullName)
            .ToArray();

        Assert.Equal(SeedTestData.DefaultBoardOrder, board);

        WaitingTime Wait(string key)
        {
            var patient = patients[key];
            return WaitingTimeEvaluator.Evaluate(patient.Status, patient.ArrivalTime, patient.TreatmentStartedAt, SeedTestData.Now);
        }

        WaitingLevel Level(string key) => Wait(key).Level;
        Assert.Equal(WaitingLevel.Normal, Level("alpha"));
        Assert.Equal(WaitingLevel.Warning, Level("charlie"));
        Assert.Equal(WaitingLevel.Critical, Level("hotel"));
        Assert.Equal(WaitingLevel.None, Level("bravo"));
        Assert.Equal(10, Wait("bravo").Minutes);
    }

    private static async Task<Dictionary<string, Patient>> CreateAllAsync()
    {
        var seeds = (await FictionalPatientSeedLoader.LoadAsync(SeedTestData.CuratedFilePath, CancellationToken.None)).Valid;
        return seeds.ToDictionary(s => s.SeedKey, s => FictionalPatientFactory.Create(s, SeedTestData.Now));
    }
}
