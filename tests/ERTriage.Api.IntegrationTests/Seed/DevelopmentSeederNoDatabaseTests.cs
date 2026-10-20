using ERTriage.Api.Configuration;
using ERTriage.Api.IntegrationTests.Persistence;
using ERTriage.Application;
using ERTriage.Application.Common;
using ERTriage.Infrastructure;
using ERTriage.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ERTriage.Api.IntegrationTests.Seed;

/// <summary>
/// Seeder paths that must finish <b>before any database work</b>. The connection string points at a closed port with a
/// 1-second connect timeout, so any attempt to open it would fail these tests.
/// </summary>
public sealed class DevelopmentSeederNoDatabaseTests : IDisposable
{
    private const string Unreachable = "Server=tcp:127.0.0.1,1;Database=ERTriage_Test_nodb;User Id=x;Password=x;Connect Timeout=1;TrustServerCertificate=True";

    private readonly CapturingLoggerProvider _logs = new();
    private readonly List<string> _files = [];

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"patients\":[", "MalformedDocument")]
    [InlineData("{\"schemaVersion\":2,\"patients\":[]}", "UnsupportedSchemaVersion")]
    [InlineData("{\"schemaVersion\":1,\"patients\":[]}", "NoRecords")]
    public async Task UnusableDocument_IsADocumentLevelFailure_WithoutDatabaseWork(string json, string code)
    {
        var result = await SeedAsync(TempFile(json));

        Assert.Equal(DevelopmentSeedOutcome.Failed, result.Outcome);
        Assert.True(result.IsDocumentFailure);
        Assert.Equal(code, result.DocumentErrorCode);
        Assert.Equal((0, 0, 0, 0, 0), (result.TotalRecords, result.Inserted, result.AlreadyExisting, result.Skipped, result.Failed));
        var error = Assert.Single(_logs.Entries, e => e.Message.Contains("seed document is unusable", StringComparison.Ordinal));
        Assert.Contains(code, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingFile_IsADocumentLevelFailure()
    {
        var result = await SeedAsync(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));

        Assert.Equal("FileNotFound", result.DocumentErrorCode);
    }

    [Fact]
    public async Task EveryRecordInvalid_IsARecordLevelFailure_WithoutDatabaseWork()
    {
        var first = SeedTestData.WaitingPatient("first");
        first["age"] = 500;
        var second = SeedTestData.WaitingPatient("second");
        second["status"] = "Unknown";

        var result = await SeedAsync(TempFile(SeedTestData.File(first, second)));

        Assert.Equal(DevelopmentSeedOutcome.Failed, result.Outcome);
        Assert.False(result.IsDocumentFailure);
        Assert.Equal((2, 0, 2), (result.TotalRecords, result.Inserted, result.Skipped));
        Assert.Equal(2, _logs.Entries.Count(e => e.Message.Contains("skipped record", StringComparison.Ordinal)));
        Assert.Single(_logs.Entries, e => e.Message.Contains("TotalRecords 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelledBeforeStart_ThrowsCancellation_WithoutAFatalErrorLog()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SeedAsync(SeedTestData.ShippedFilePath, new CancellationToken(canceled: true)));

        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains("failed", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        foreach (var file in _files)
        {
            File.Delete(file);
        }
    }

    private async Task<DevelopmentSeedResult> SeedAsync(string dataFile, CancellationToken cancellationToken = default)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ApiConfiguration.ConnectionStringKey] = Unreachable })
            .Build();

        await using var provider = new ServiceCollection()
            .AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddProvider(_logs);
            })
            .AddSingleton<TimeProvider>(new FixedTimeProvider(SeedTestData.Now))
            .AddSingleton(new DevelopmentSeedOptions { DataFilePath = dataFile })
            .AddApplication()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDevelopmentDataSeeder>().SeedAsync(cancellationToken);
    }

    private string TempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"seed-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        _files.Add(path);
        return path;
    }
}
