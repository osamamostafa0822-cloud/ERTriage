using ERTriage.Api.Configuration;
using ERTriage.Api.Startup;
using ERTriage.Application.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ERTriage.Api.IntegrationTests.Seed;

/// <summary>
/// Startup orchestration of the Development seed (<see cref="DevelopmentSeedStartup"/>): both gates, own scope with
/// async disposal, cancellation, propagation of fatal errors, and explicit handling of every outcome. No database.
/// </summary>
public class DevelopmentSeedStartupTests
{
    private static readonly DevelopmentSeedResult Completed =
        new(Guid.NewGuid(), DevelopmentSeedOutcome.Completed, 12, 12, 0, 0, 0);

    [Theory]
    [InlineData("Development", "true", true)]
    [InlineData("development", "true", true)]
    [InlineData("Development", "false", false)]
    [InlineData("Development", null, false)]
    [InlineData("Development", "maybe", false)]
    [InlineData("Test", "true", false)]
    [InlineData("Production", "true", false)]
    public async Task Gates_SeedOnlyInDevelopmentWithTheFlag(string environment, string? flag, bool expectedRun)
    {
        var seeder = new FakeSeeder(_ => Task.FromResult(Completed));
        await using var services = Services(seeder);

        var result = await DevelopmentSeedStartup.RunIfEnabledAsync(services, Env(environment), Config(flag), CancellationToken.None);

        Assert.Equal(expectedRun ? 1 : 0, seeder.Calls);
        Assert.Equal(expectedRun, result is not null);
        Assert.Equal(expectedRun, DevelopmentSeedStartup.IsEnabled(Env(environment), Config(flag)));
    }

    [Fact]
    public async Task Seeder_RunsInItsOwnScope_WhichIsDisposedAsynchronously()
    {
        var seeder = new FakeSeeder(_ => Task.FromResult(Completed));
        await using var services = Services(seeder); // ValidateScopes: a scoped service cannot come from the root

        await DevelopmentSeedStartup.RunIfEnabledAsync(services, Env("Development"), Config("true"), CancellationToken.None);

        Assert.Equal(1, seeder.Calls);
        Assert.True(seeder.DisposedAsync);
    }

    [Fact]
    public async Task CancellationToken_IsPassedToTheSeeder()
    {
        using var cts = new CancellationTokenSource();
        var seeder = new FakeSeeder(_ => Task.FromResult(Completed));
        await using var services = Services(seeder);

        await DevelopmentSeedStartup.RunIfEnabledAsync(services, Env("Development"), Config("true"), cts.Token);

        Assert.Equal(cts.Token, seeder.ReceivedToken);
    }

    [Fact]
    public async Task AlreadyCancelled_DoesNotStartTheSeeder()
    {
        var seeder = new FakeSeeder(_ => Task.FromResult(Completed));
        await using var services = Services(seeder);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DevelopmentSeedStartup.RunIfEnabledAsync(services, Env("Development"), Config("true"), new CancellationToken(canceled: true)));

        Assert.Equal(0, seeder.Calls);
    }

    [Fact]
    public async Task CancelledWhileSeeding_PropagatesCancellation_AndStillDisposesTheScope()
    {
        using var cts = new CancellationTokenSource();
        var seeder = new FakeSeeder(async token =>
        {
            await cts.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            return Completed;
        });
        await using var services = Services(seeder);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DevelopmentSeedStartup.RunIfEnabledAsync(services, Env("Development"), Config("true"), cts.Token));

        Assert.True(seeder.DisposedAsync);
    }

    [Fact]
    public async Task FatalError_IsNotSwallowed_TheSameExceptionPropagates()
    {
        var fatal = new InvalidOperationException("simulated infrastructure failure");
        var seeder = new FakeSeeder(_ => throw fatal);
        await using var services = Services(seeder);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DevelopmentSeedStartup.RunIfEnabledAsync(services, Env("Development"), Config("true"), CancellationToken.None));

        Assert.Same(fatal, thrown);
        Assert.True(seeder.DisposedAsync);
    }

    [Theory]
    [InlineData(DevelopmentSeedOutcome.Completed)]
    [InlineData(DevelopmentSeedOutcome.PartiallyCompleted)]
    [InlineData(DevelopmentSeedOutcome.AlreadyComplete)]
    [InlineData(DevelopmentSeedOutcome.LockNotAcquired)]
    [InlineData(DevelopmentSeedOutcome.Failed)]
    public async Task EveryKnownOutcome_LetsTheApplicationStart(DevelopmentSeedOutcome outcome)
    {
        var expected = Completed with { Outcome = outcome };
        var seeder = new FakeSeeder(_ => Task.FromResult(expected));
        await using var services = Services(seeder);

        var result = await DevelopmentSeedStartup.RunIfEnabledAsync(services, Env("Development"), Config("true"), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public void UnknownOutcome_StopsTheStartup()
    {
        var unknown = Completed with { Outcome = (DevelopmentSeedOutcome)99 };

        var ex = Assert.Throws<InvalidOperationException>(() => DevelopmentSeedStartup.Handle(unknown));

        Assert.Contains("99", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Result_DistinguishesDocumentFailuresFromRecordFailures()
    {
        var document = Completed with { Outcome = DevelopmentSeedOutcome.Failed, DocumentErrorCode = "MalformedDocument" };
        var records = Completed with { Outcome = DevelopmentSeedOutcome.Failed, Skipped = 12, Inserted = 0 };

        Assert.True(document.IsDocumentFailure);
        Assert.False(records.IsDocumentFailure);
    }

    private static ServiceProvider Services(FakeSeeder seeder) =>
        new ServiceCollection()
            .AddScoped<IDevelopmentDataSeeder>(_ => seeder)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    private static IConfiguration Config(string? flag)
    {
        var values = new Dictionary<string, string?>();
        if (flag is not null)
        {
            values[ApiConfiguration.SeedDevelopmentDataKey] = flag;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static IHostEnvironment Env(string name) => new FakeHostEnvironment(name);

    private sealed class FakeHostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "ERTriage.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>Scoped fake; the container disposes it with the scope (async), which the tests observe.</summary>
    private sealed class FakeSeeder(Func<CancellationToken, Task<DevelopmentSeedResult>> behaviour)
        : IDevelopmentDataSeeder, IAsyncDisposable
    {
        public int Calls { get; private set; }

        public CancellationToken ReceivedToken { get; private set; }

        public bool DisposedAsync { get; private set; }

        public Task<DevelopmentSeedResult> SeedAsync(CancellationToken cancellationToken)
        {
            Calls++;
            ReceivedToken = cancellationToken;
            return behaviour(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            DisposedAsync = true;
            return ValueTask.CompletedTask;
        }
    }
}
