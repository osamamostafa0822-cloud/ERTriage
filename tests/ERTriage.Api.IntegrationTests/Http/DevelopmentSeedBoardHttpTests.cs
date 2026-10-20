using ERTriage.Api.IntegrationTests.Persistence;
using ERTriage.Api.IntegrationTests.Seed;
using ERTriage.Application.Common;
using Microsoft.Extensions.DependencyInjection;

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

    // The Test host registers the seeder (it is only *invoked* by Program.cs in Development), with the fixed clock.
    private async Task<DevelopmentSeedResult> SeedThroughTheHostAsync()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDevelopmentDataSeeder>().SeedAsync(CancellationToken.None);
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
