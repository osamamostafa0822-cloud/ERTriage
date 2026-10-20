using ERTriage.Api.IntegrationTests.Persistence;
using ERTriage.Api.IntegrationTests.Seed;
using ERTriage.Application.Common;
using ERTriage.Infrastructure.Persistence;
using ERTriage.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ERTriage.Api.IntegrationTests.Http;

/// <summary>
/// The seed as the frontend will see it: the Test host never seeds by itself; when the fixed-clock test database is
/// seeded explicitly through the same seeder, <c>GET /api/triage-board</c> returns the approved order and pages.
/// </summary>
public sealed class DevelopmentSeedBoardHttpTests(ApiTestFixture fixture)
    : HttpTestBase(fixture), IClassFixture<ApiTestFixture>
{
    [SqlServerFact]
    public async Task Board_AfterSeeding_ReturnsTheApprovedOrderPagesAndWaitingLevels()
    {
        await SeedThroughTheHostAsync();

        var first = await GetBoardAsync();
        Assert.Equal(10, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(SeedTestData.DefaultBoardOrder, Names(first));

        Assert.Equal(SeedTestData.DefaultBoardOrder[..4], Names(await GetBoardAsync("?pageSize=4&pageNumber=1")));
        Assert.Equal(SeedTestData.DefaultBoardOrder[4..8], Names(await GetBoardAsync("?pageSize=4&pageNumber=2")));
        Assert.Equal(SeedTestData.DefaultBoardOrder[8..], Names(await GetBoardAsync("?pageSize=4&pageNumber=3")));
        Assert.Equal(SeedTestData.CompletedBoardOrder, Names(await GetBoardAsync("?status=Discharged&status=Admitted")));

        var levels = first.GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("fullName").GetString() ?? "", i => i.GetProperty("waitingLevel").GetString());
        Assert.Equal("Normal", levels["Test Patient Alpha"]);
        Assert.Equal("Warning", levels["Test Patient Charlie"]);
        Assert.Equal("Critical", levels["Test Patient Hotel"]);
        Assert.Equal("None", levels["Test Patient Bravo"]);

        var india = first.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("fullName").GetString() == "Test Patient India");
        Assert.True(india.GetProperty("isOverridden").GetBoolean());
        Assert.Equal(2, india.GetProperty("currentPriority").GetInt32());
    }

    [SqlServerFact]
    public async Task Seeding_TwiceThroughTheHost_DoesNotDuplicateBoardRows()
    {
        await SeedThroughTheHostAsync();
        var second = await SeedThroughTheHostAsync();

        Assert.Equal(DevelopmentSeedOutcome.AlreadyComplete, second.Outcome);
        Assert.Equal(10, (await GetBoardAsync()).GetProperty("totalCount").GetInt32());
    }

    [SqlServerFact]
    public async Task Board_AfterSeedingTheFullFile_PagesThroughEveryActivePatient()
    {
        await SeedThroughTheHostAsync(SeedTestData.ShippedFilePath);
        var counts = SeedTestData.ShippedStatusCounts();
        var expectedActive = counts["Waiting"] + counts["InTreatment"];
        var lastPage = (expectedActive + 99) / 100;

        var first = await GetBoardAsync("?pageSize=100");
        Assert.Equal(expectedActive, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(100, first.GetProperty("items").GetArrayLength());

        var last = await GetBoardAsync($"?pageSize=100&pageNumber={lastPage}");
        Assert.Equal(expectedActive - (lastPage - 1) * 100, last.GetProperty("items").GetArrayLength());
        Assert.Equal(0, (await GetBoardAsync($"?pageSize=100&pageNumber={lastPage + 1}")).GetProperty("items").GetArrayLength());

        var completed = await GetBoardAsync("?status=Discharged&status=Admitted&pageSize=1");
        Assert.Equal(counts["Discharged"] + counts["Admitted"], completed.GetProperty("totalCount").GetInt32());
    }

    // Runs the real seeder with the Test host's services (fixed clock, isolated database) and the chosen data file;
    // the host itself never seeds (it is only invoked by Program.cs in Development).
    private async Task<DevelopmentSeedResult> SeedThroughTheHostAsync(string? dataFile = null)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var seeder = new DevelopmentDataSeeder(
            scope.ServiceProvider.GetRequiredService<ErTriageDbContext>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            new DevelopmentSeedOptions { DataFilePath = dataFile ?? SeedTestData.CuratedFilePath },
            scope.ServiceProvider.GetRequiredService<ILogger<DevelopmentDataSeeder>>());
        return await seeder.SeedAsync(CancellationToken.None);
    }
}

/// <summary>
/// Own class (own fixture, own database, one test) so the startup logs it inspects are not affected by other tests:
/// the Test host must never invoke the seeder by itself.
/// </summary>
public sealed class TestHostNeverSeedsHttpTests(ApiTestFixture fixture)
    : HttpTestBase(fixture), IClassFixture<ApiTestFixture>
{
    [SqlServerFact]
    public async Task TestHost_DoesNotSeedAtStartup()
    {
        Assert.NotEmpty(Fixture.Logs.Entries); // the capture works, so an absent seeder entry is meaningful
        Assert.DoesNotContain(Fixture.Logs.Entries, e => e.Category.Contains("DevelopmentDataSeeder", StringComparison.Ordinal));
        Assert.Equal(0, (await GetBoardAsync()).GetProperty("totalCount").GetInt32());
    }
}
