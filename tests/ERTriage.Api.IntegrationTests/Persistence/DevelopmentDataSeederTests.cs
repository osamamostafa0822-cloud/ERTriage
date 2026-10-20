using System.Data;
using System.Text.Json.Nodes;
using ERTriage.Api.Configuration;
using ERTriage.Api.IntegrationTests.Seed;
using ERTriage.Application;
using ERTriage.Application.Common;
using ERTriage.Domain.Common;
using ERTriage.Domain.Patients;
using ERTriage.Infrastructure;
using ERTriage.Infrastructure.Persistence;
using ERTriage.Infrastructure.Persistence.Seed;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ERTriage.Application.Patients.Repositories;
using ERTriage.Application.Triage.Queries;

namespace ERTriage.Api.IntegrationTests.Persistence;

/// <summary>
/// Best-effort Development seeder on a real, isolated SQL Server database (generated <c>ERTriage_Test_{hex}</c>, real
/// migrations): per-record skip and failure, retry on the next run, idempotency, restore by seed key, never touching
/// other rows, concurrency, the application lock, fatal errors, content, board order and safe logs.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class DevelopmentDataSeederTests(SqlServerFixture fixture) : DatabaseTestBase(fixture)
{
    private readonly FixedTimeProvider _clock = new(SeedTestData.Now);
    private readonly CapturingLoggerProvider _logs = new();
    private readonly List<string> _tempFiles = [];

    public override Task DisposeAsync()
    {
        foreach (var file in _tempFiles)
        {
            File.Delete(file);
        }

        return base.DisposeAsync();
    }

    // ---- the fully valid file ----------------------------------------------------------------------------------

    [SqlServerFact]
    public async Task ValidFile_InsertsTwelvePatientsAndTwelveMarkers()
    {
        var result = await SeedAsync();

        AssertCounts(result, DevelopmentSeedOutcome.Completed, total: 12, inserted: 12, existing: 0, skipped: 0, failed: 0);
        Assert.Equal(12, await CountAsync("Patients"));
        Assert.Equal(12, await CountAsync("DevelopmentSeedRecords"));
        Assert.Equal(12, await ScalarIntAsync("SELECT COUNT(*) FROM DevelopmentSeedRecords r JOIN Patients p ON p.Id = r.PatientId"));
    }

    [SqlServerFact]
    public async Task SecondRun_WritesNothingAndKeepsEveryRowUnchanged()
    {
        await SeedAsync();
        var before = await RowVersionsAsync();

        var result = await SeedAsync();

        AssertCounts(result, DevelopmentSeedOutcome.AlreadyComplete, total: 12, inserted: 0, existing: 12, skipped: 0, failed: 0);
        Assert.Equal(before, await RowVersionsAsync());
    }

    [SqlServerFact]
    public async Task ExistingNonSeedPatient_IsNeverModifiedAndTheSeedStillRuns()
    {
        var own = PersistenceTestData.NewPatient(_clock, "Developer Registered Patient");
        await using (var provider = BuildProvider())
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IPatientRepository>().Add(own);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        var ownBefore = await RowVersionOfAsync(own.Id);

        var result = await SeedAsync();

        Assert.Equal(12, result.Inserted);
        Assert.Equal(13, await CountAsync("Patients"));
        Assert.Equal(ownBefore, await RowVersionOfAsync(own.Id));
    }

    // ---- best-effort: skipped records ---------------------------------------------------------------------------

    [SqlServerFact]
    public async Task OneInvalidRecord_InsertsElevenAndSkipsOne()
    {
        var path = WriteSeedFile(patients => Record(patients, "delta")["age"] = 999);

        var result = await SeedAsync(new DevelopmentSeedOptions { DataFilePath = path });

        AssertCounts(result, DevelopmentSeedOutcome.PartiallyCompleted, total: 12, inserted: 11, existing: 0, skipped: 1, failed: 0);
        Assert.Equal(11, await CountAsync("Patients"));
        Assert.Equal(0, await ScalarIntAsync("SELECT COUNT(*) FROM DevelopmentSeedRecords WHERE SeedKey = 'delta'"));
        Assert.Equal(0, await ScalarIntAsync("SELECT COUNT(*) FROM Patients WHERE FullName = N'Test Patient Delta'"));
    }

    [SqlServerFact]
    public async Task SeveralInvalidRecords_InsertTheRemainingValidOnes()
    {
        var path = WriteSeedFile(patients =>
        {
            Record(patients, "delta")["age"] = 999;
            Record(patients, "golf")["status"] = "Sleeping";
            Record(patients, "kilo").Remove("dispositionNote");
        });

        var result = await SeedAsync(new DevelopmentSeedOptions { DataFilePath = path });

        AssertCounts(result, DevelopmentSeedOutcome.PartiallyCompleted, total: 12, inserted: 9, existing: 0, skipped: 3, failed: 0);
        Assert.Equal(9, await CountAsync("DevelopmentSeedRecords"));
    }

    [SqlServerFact]
    public async Task CorrectedFile_InsertsOnlyThePreviouslySkippedRecord()
    {
        var broken = WriteSeedFile(patients => Record(patients, "delta")["age"] = 999);
        await SeedAsync(new DevelopmentSeedOptions { DataFilePath = broken });
        var before = await RowVersionsAsync();

        var result = await SeedAsync(); // the corrected (curated) file

        AssertCounts(result, DevelopmentSeedOutcome.Completed, total: 12, inserted: 1, existing: 11, skipped: 0, failed: 0);
        var after = await RowVersionsAsync();
        Assert.Equal(12, after.Count);
        Assert.All(before, row => Assert.Equal(row.Value, after[row.Key]));
        Assert.Equal(1, await ScalarIntAsync("SELECT COUNT(*) FROM DevelopmentSeedRecords WHERE SeedKey = 'delta'"));
    }

    [SqlServerFact]
    public async Task DuplicateSeedKeys_SkipEveryRecordSharingTheKey()
    {
        var path = WriteSeedFile(patients => Record(patients, "golf")["seedKey"] = "foxtrot");

        var result = await SeedAsync(new DevelopmentSeedOptions { DataFilePath = path });

        AssertCounts(result, DevelopmentSeedOutcome.PartiallyCompleted, total: 12, inserted: 10, existing: 0, skipped: 2, failed: 0);
        Assert.Equal(0, await ScalarIntAsync("SELECT COUNT(*) FROM DevelopmentSeedRecords WHERE SeedKey = 'foxtrot'"));
    }

    [SqlServerFact]
    public async Task UnusableDocument_FailsWithoutWriting()
    {
        var path = TempFile("{\"schemaVersion\":1,\"patients\":[");

        var result = await SeedAsync(new DevelopmentSeedOptions { DataFilePath = path });

        Assert.Equal(DevelopmentSeedOutcome.Failed, result.Outcome);
        Assert.Equal("MalformedDocument", result.DocumentErrorCode);
        Assert.Equal(0, await CountAsync("Patients"));
        Assert.Contains(_logs.Entries, e => e.Message.Contains("MalformedDocument", StringComparison.Ordinal));
    }

    [SqlServerFact]
    public async Task EveryRecordInvalid_ReportsFailed()
    {
        var bad = SeedTestData.WaitingPatient("only");
        bad["age"] = 999;

        var result = await SeedAsync(new DevelopmentSeedOptions { DataFilePath = TempFile(SeedTestData.File(bad)) });

        AssertCounts(result, DevelopmentSeedOutcome.Failed, total: 1, inserted: 0, existing: 0, skipped: 1, failed: 0);
        Assert.False(result.IsDocumentFailure); // record-level, not document-level
        Assert.Equal(0, await CountAsync("Patients"));
    }

    // ---- best-effort: records that fail after validation --------------------------------------------------------

    [SqlServerFact]
    public async Task DomainExceptionInOneRecord_DoesNotAffectTheOthers()
    {
        var result = await SeedWithFactoryAsync((seed, now) => seed.SeedKey == "echo"
            ? throw new DomainValidationException("Simulated domain rule violation")
            : FictionalPatientFactory.Create(seed, now));

        AssertCounts(result, DevelopmentSeedOutcome.PartiallyCompleted, total: 12, inserted: 11, existing: 0, skipped: 0, failed: 1);
        Assert.Equal(0, await ScalarIntAsync("SELECT COUNT(*) FROM DevelopmentSeedRecords WHERE SeedKey = 'echo'"));
        Assert.Contains(_logs.Entries, e => e.Message.Contains("DomainRuleViolation", StringComparison.Ordinal));

        var retry = await SeedAsync(); // next start: only the failed record is retried
        AssertCounts(retry, DevelopmentSeedOutcome.Completed, total: 12, inserted: 1, existing: 11, skipped: 0, failed: 0);
    }

    [SqlServerFact]
    public async Task RecoverableDatabaseError_RollsBackOnlyThatRecord()
    {
        // A row with the same primary key appears just before 'foxtrot' is saved: SQL Server raises 2627 for that
        // statement only. The record is rolled back to its savepoint (no patient, no marker); the others commit.
        var conflictingId = Guid.Empty;
        var result = await SeedWithFactoryAsync((seed, now) =>
        {
            var patient = FictionalPatientFactory.Create(seed, now);
            if (seed.SeedKey == "foxtrot")
            {
                conflictingId = patient.Id;
                InsertConflictingRow(patient.Id);
            }

            return patient;
        });

        AssertCounts(result, DevelopmentSeedOutcome.PartiallyCompleted, total: 12, inserted: 11, existing: 0, skipped: 0, failed: 1);
        Assert.Equal(12, await CountAsync("Patients")); // 11 seeded + the untouched conflicting row
        Assert.Equal("Conflicting Row", await ScalarAsync("SELECT FullName FROM Patients WHERE Id = @id", IdParameter(conflictingId)));
        Assert.Equal(0, await ScalarIntAsync("SELECT COUNT(*) FROM DevelopmentSeedRecords WHERE SeedKey = 'foxtrot'"));
        Assert.Contains(_logs.Entries, e => e.Message.Contains("DatabaseConstraint", StringComparison.Ordinal)
                                            && e.Message.Contains("2627", StringComparison.Ordinal));
    }

    [SqlServerFact]
    public async Task FatalDatabaseError_StopsSeeding_IsLoggedAndRethrown_AndCommitsNothing()
    {
        var ex = await Assert.ThrowsAsync<SqlException>(() => SeedWithFactoryAsync((seed, now) =>
        {
            if (seed.SeedKey == "golf")
            {
                RaiseSqlError(); // a non-row-level database failure
            }

            return FictionalPatientFactory.Create(seed, now);
        }));

        Assert.Equal(8134, ex.Number); // divide by zero: the original exception is preserved
        Assert.Equal(0, await CountAsync("Patients"));
        Assert.Equal(0, await CountAsync("DevelopmentSeedRecords"));
        Assert.Contains(_logs.Entries, e => e.Message.Contains("failed fatally", StringComparison.Ordinal));
    }

    [SqlServerFact]
    public async Task CancelledMidRun_PropagatesCancellation_CommitsNothing_AndIsNotLoggedAsFatal()
    {
        using var cts = new CancellationTokenSource();
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var seeder = new DevelopmentDataSeeder(
            scope.ServiceProvider.GetRequiredService<ErTriageDbContext>(),
            _clock,
            Curated(),
            scope.ServiceProvider.GetRequiredService<ILogger<DevelopmentDataSeeder>>(),
            (seed, now) =>
            {
                if (seed.SeedKey == "golf")
                {
                    cts.Cancel(); // the host starts stopping while records are being saved
                }

                return FictionalPatientFactory.Create(seed, now);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => seeder.SeedAsync(cts.Token));

        Assert.Equal(0, await CountAsync("Patients"));
        Assert.Equal(0, await CountAsync("DevelopmentSeedRecords"));
        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains("failed fatally", StringComparison.Ordinal));
    }

    // ---- restore by seed key (C3) -------------------------------------------------------------------------------

    [SqlServerFact]
    public async Task DeletedSeedPatient_IsRecreatedWithTheSameSeedKey_OthersUntouched()
    {
        await SeedAsync();
        var alphaBefore = await PatientIdForKeyAsync("alpha");
        await ExecuteAsync("DELETE FROM Patients WHERE Id = @id", IdParameter(alphaBefore)); // cascade removes the marker
        Assert.Equal(0, await ScalarIntAsync("SELECT COUNT(*) FROM DevelopmentSeedRecords WHERE SeedKey = 'alpha'"));
        var othersBefore = await RowVersionsAsync();

        var result = await SeedAsync();

        AssertCounts(result, DevelopmentSeedOutcome.Completed, total: 12, inserted: 1, existing: 11, skipped: 0, failed: 0);
        Assert.NotEqual(alphaBefore, await PatientIdForKeyAsync("alpha"));
        var after = await RowVersionsAsync();
        Assert.All(othersBefore, row => Assert.Equal(row.Value, after[row.Key]));
    }

    [SqlServerFact]
    public async Task MarkerAndPatientDeleted_AreRecreatedWithTheSameSeedKey()
    {
        await SeedAsync();
        var echo = await PatientIdForKeyAsync("echo");
        await ExecuteAsync("DELETE FROM DevelopmentSeedRecords WHERE SeedKey = 'echo'");
        await ExecuteAsync("DELETE FROM Patients WHERE Id = @id", IdParameter(echo));

        var result = await SeedAsync();

        Assert.Equal(1, result.Inserted);
        Assert.Equal(12, await CountAsync("Patients"));
        Assert.NotEqual(echo, await PatientIdForKeyAsync("echo"));
    }

    [SqlServerFact]
    public async Task OnlyMarkerDeleted_AddsANewPatientAndLeavesTheOldRowUntouched()
    {
        await SeedAsync();
        var oldBravo = await PatientIdForKeyAsync("bravo");
        var oldVersion = await RowVersionOfAsync(oldBravo);
        await ExecuteAsync("DELETE FROM DevelopmentSeedRecords WHERE SeedKey = 'bravo'");

        var result = await SeedAsync();

        Assert.Equal(1, result.Inserted);
        Assert.Equal(13, await CountAsync("Patients"));
        Assert.Equal(oldVersion, await RowVersionOfAsync(oldBravo)); // documented: the orphan stays as ordinary data
    }

    // ---- concurrency and the application lock -------------------------------------------------------------------

    [SqlServerFact]
    public async Task FourConcurrentRuns_InsertEachPatientExactlyOnce()
    {
        await using var provider = BuildProvider();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IDevelopmentDataSeeder>().SeedAsync(CancellationToken.None);
        }));

        Assert.Equal(12, results.Sum(r => r.Inserted));
        Assert.Single(results, r => r.Outcome == DevelopmentSeedOutcome.Completed);
        Assert.All(results.Where(r => r.Outcome != DevelopmentSeedOutcome.Completed),
            r => Assert.Equal(DevelopmentSeedOutcome.AlreadyComplete, r.Outcome));
        Assert.Equal(12, await CountAsync("Patients"));
        Assert.Equal(12, await CountAsync("DevelopmentSeedRecords"));
    }

    [SqlServerFact]
    public async Task LockHeldByAnotherTransaction_TimesOutWithoutWriting_ThenSeedsAfterRelease()
    {
        await using (var holder = await Fixture.OpenConnectionAsync())
        await using (var holderTransaction = (SqlTransaction)await holder.BeginTransactionAsync())
        {
            Assert.True(await GetAppLockAsync(holder, holderTransaction) >= 0);

            var blocked = await SeedAsync(Curated(lockTimeout: TimeSpan.FromSeconds(1)));

            Assert.Equal(DevelopmentSeedOutcome.LockNotAcquired, blocked.Outcome);
            Assert.Equal(0, blocked.Inserted);
            Assert.Equal(0, await CountAsync("Patients"));

            await holderTransaction.RollbackAsync(); // releases the transaction-owned lock
        }

        var released = await SeedAsync(Curated(lockTimeout: TimeSpan.FromSeconds(1)));

        Assert.Equal(12, released.Inserted);
    }

    // ---- content, timestamps, board order and pagination against the real repository ---------------------------

    [SqlServerFact]
    public async Task SeededRows_HaveTheApprovedPrioritiesStatusesAndOrderedTimestamps()
    {
        await SeedAsync();

        var rows = await ReadAsync(
            "SELECT r.SeedKey, p.CalculatedPriority, p.CurrentPriority, p.Status, p.ArrivalTime, " +
            "p.TreatmentStartedAt, p.CompletedAt, p.OverriddenAt, p.FullName " +
            "FROM DevelopmentSeedRecords r JOIN Patients p ON p.Id = r.PatientId",
            r => (Key: r.GetString(0), Calculated: (int)r.GetByte(1), Current: (int)r.GetByte(2), Status: r.GetString(3),
                Arrival: r.GetDateTime(4), Started: r.IsDBNull(5) ? (DateTime?)null : r.GetDateTime(5),
                Completed: r.IsDBNull(6) ? (DateTime?)null : r.GetDateTime(6),
                Overridden: r.IsDBNull(7) ? (DateTime?)null : r.GetDateTime(7), Name: r.GetString(8)));

        var now = SeedTestData.Now.UtcDateTime;
        Assert.Equal(12, rows.Count);
        foreach (var row in rows)
        {
            Assert.Equal(SeedTestData.CalculatedPriority[row.Key], row.Calculated);
            Assert.Equal(SeedTestData.Status[row.Key].ToString(), row.Status);
            Assert.StartsWith("Test Patient ", row.Name, StringComparison.Ordinal);
            Assert.True(row.Arrival <= now);
            if (row.Started is { } started) Assert.InRange(started, row.Arrival, now);
            if (row.Completed is { } completed) Assert.InRange(completed, row.Started!.Value, now);
            if (row.Overridden is { } overridden) Assert.InRange(overridden, row.Arrival, now);
        }

        var india = Assert.Single(rows, r => r.Key == "india");
        Assert.Equal((4, 2), (india.Calculated, india.Current));
    }

    [SqlServerFact]
    public async Task Board_OnTheFullyValidSeed_MatchesTheApprovedOrderAndPagination()
    {
        await SeedAsync();
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPatientRepository>();
        PatientStatus[] active = [PatientStatus.Waiting, PatientStatus.InTreatment];

        PatientStatus[] completed = [PatientStatus.Discharged, PatientStatus.Admitted];

        // The expected total is passed explicitly: both filters have two statuses, so it cannot be derived from them.
        async Task<string[]> Page(PatientStatus[] statuses, int number, int size, int expectedTotal)
        {
            var page = await repository.GetBoardPageAsync(
                new TriageBoardQuery(statuses, new PaginationRequest(number, size)), CancellationToken.None);
            Assert.Equal(expectedTotal, page.TotalCount);
            return page.Items.Select(p => p.FullName).ToArray();
        }

        Assert.Equal(SeedTestData.DefaultBoardOrder, await Page(active, 1, 10, expectedTotal: 10));
        Assert.Equal(SeedTestData.DefaultBoardOrder[..4], await Page(active, 1, 4, expectedTotal: 10));
        Assert.Equal(SeedTestData.DefaultBoardOrder[4..8], await Page(active, 2, 4, expectedTotal: 10));
        Assert.Equal(SeedTestData.DefaultBoardOrder[8..], await Page(active, 3, 4, expectedTotal: 10));
        Assert.Empty(await Page(active, 4, 4, expectedTotal: 10));
        Assert.Equal(SeedTestData.CompletedBoardOrder, await Page(completed, 1, 10, expectedTotal: 2));
    }

    // ---- schema and logs ----------------------------------------------------------------------------------------

    [SqlServerFact]
    public async Task MarkerTable_HasPrimaryKeyUniquePatientIdAndCascadingForeignKey()
    {
        var columns = await ReadAsync(
            "SELECT COLUMN_NAME + ':' + DATA_TYPE + ':' + COALESCE(CAST(CHARACTER_MAXIMUM_LENGTH AS varchar(10)), '-') + ':' + IS_NULLABLE " +
            "FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'DevelopmentSeedRecords' ORDER BY ORDINAL_POSITION",
            r => r.GetString(0));
        Assert.Equal(["SeedKey:varchar:50:NO", "PatientId:uniqueidentifier:-:NO", "SeededAt:datetime2:-:NO"], columns);

        var indexes = await ReadAsync(
            "SELECT name + ':' + CAST(is_primary_key AS varchar(1)) + ':' + CAST(is_unique AS varchar(1)) FROM sys.indexes " +
            "WHERE object_id = OBJECT_ID(N'DevelopmentSeedRecords') AND type > 0 ORDER BY name",
            r => r.GetString(0));
        Assert.Equal(["PK_DevelopmentSeedRecords:1:1", "UX_DevelopmentSeedRecords_PatientId:0:1"], indexes);

        Assert.Equal("CASCADE", await ScalarAsync(
            "SELECT delete_referential_action_desc FROM sys.foreign_keys WHERE name = 'FK_DevelopmentSeedRecords_Patients_PatientId' " +
            "AND referenced_object_id = OBJECT_ID(N'Patients')"));
    }

    [SqlServerFact]
    public async Task Logs_CarryRunIdCodesFieldsAndCounts_NeverPatientData()
    {
        var path = WriteSeedFile(patients => Record(patients, "delta")["age"] = 987);

        var result = await SeedAsync(new DevelopmentSeedOptions { DataFilePath = path });

        var runId = result.SeedRunId.ToString();
        var skipped = Assert.Single(_logs.Entries, e => e.Message.Contains("skipped record", StringComparison.Ordinal));
        Assert.Contains("delta", skipped.Message, StringComparison.Ordinal);
        Assert.Contains("OutOfRange", skipped.Message, StringComparison.Ordinal);
        Assert.Contains("age", skipped.Message, StringComparison.Ordinal);
        Assert.Contains(runId, skipped.Message, StringComparison.OrdinalIgnoreCase);

        var summary = Assert.Single(_logs.Entries, e => e.Message.Contains("TotalRecords", StringComparison.Ordinal));
        foreach (var part in new[] { "PartiallyCompleted", "TotalRecords 12", "InsertedCount 11", "AlreadyExistingCount 0", "SkippedCount 1", "FailedCount 0", runId })
        {
            Assert.Contains(part, summary.Message, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var forbidden in new[] { "Test Patient", "987", "Fever", "\"age\"", "{" })
        {
            Assert.DoesNotContain(_logs.Entries, e => e.Category.Contains("DevelopmentDataSeeder", StringComparison.Ordinal)
                                                      && e.Message.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    // ---- the full shipped file (5,012 records, D-63) ----

    [SqlServerFact]
    public async Task ShippedFile_InsertsEveryRecordInBatches_AndASecondRunWritesNothing()
    {
        var full = new DevelopmentSeedOptions { DataFilePath = SeedTestData.ShippedFilePath };

        var first = await SeedAsync(full);
        AssertCounts(first, DevelopmentSeedOutcome.Completed, total: 5012, inserted: 5012, existing: 0, skipped: 0, failed: 0);
        Assert.Equal(5012, await CountAsync("Patients"));
        Assert.Equal(5012, await CountAsync("DevelopmentSeedRecords"));

        var second = await SeedAsync(full);
        AssertCounts(second, DevelopmentSeedOutcome.AlreadyComplete, total: 5012, inserted: 0, existing: 5012, skipped: 0, failed: 0);
    }

    [SqlServerFact]
    public async Task ShippedFile_BoardPagesAreOrderedCompleteAndWithoutDuplicates()
    {
        await SeedAsync(new DevelopmentSeedOptions { DataFilePath = SeedTestData.ShippedFilePath });
        var counts = SeedTestData.ShippedStatusCounts();
        var expectedActive = counts["Waiting"] + counts["InTreatment"];

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPatientRepository>();
        var rows = new List<Patient>();
        for (var number = 1; ; number++)
        {
            var page = await repository.GetBoardPageAsync(
                new TriageBoardQuery([PatientStatus.Waiting, PatientStatus.InTreatment], new PaginationRequest(number, 100)),
                CancellationToken.None);
            Assert.Equal(expectedActive, page.TotalCount);
            if (page.Items.Count == 0) break;
            rows.AddRange(page.Items);
        }

        Assert.Equal(expectedActive, rows.Count);
        Assert.Equal(rows.Count, rows.Select(p => p.Id).Distinct().Count());
        for (var i = 1; i < rows.Count; i++)
        {
            var (previous, current) = (rows[i - 1], rows[i]);
            Assert.True(
                previous.CurrentPriority < current.CurrentPriority
                || (previous.CurrentPriority == current.CurrentPriority && previous.ArrivalTime <= current.ArrivalTime),
                $"Board order broken at position {i}.");
        }
    }

    [SqlServerFact]
    public async Task SmallBatches_RecoverableErrorInOneBatch_LosesOnlyThatRecord()
    {
        var result = await SeedWithFactoryAsync(
            (seed, now) =>
            {
                var patient = FictionalPatientFactory.Create(seed, now);
                if (seed.SeedKey == "hotel")
                {
                    InsertConflictingRow(patient.Id);
                }

                return patient;
            },
            Curated(batchSize: 5));

        AssertCounts(result, DevelopmentSeedOutcome.PartiallyCompleted, total: 12, inserted: 11, existing: 0, skipped: 0, failed: 1);
        Assert.Equal(11, await CountAsync("DevelopmentSeedRecords"));
        Assert.Equal(0, await ScalarIntAsync("SELECT COUNT(*) FROM DevelopmentSeedRecords WHERE SeedKey = 'hotel'"));
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private static void AssertCounts(DevelopmentSeedResult result, DevelopmentSeedOutcome outcome, int total, int inserted, int existing, int skipped, int failed)
    {
        Assert.Equal(
            (outcome, total, inserted, existing, skipped, failed),
            (result.Outcome, result.TotalRecords, result.Inserted, result.AlreadyExisting, result.Skipped, result.Failed));
        Assert.NotEqual(Guid.Empty, result.SeedRunId);
    }

    /// <summary>Options for the 12 curated records (the approved demo set).</summary>
    private static DevelopmentSeedOptions Curated(TimeSpan? lockTimeout = null, int? batchSize = null) =>
        new()
        {
            DataFilePath = SeedTestData.CuratedFilePath,
            LockTimeout = lockTimeout ?? TimeSpan.FromSeconds(30),
            BatchSize = batchSize ?? 200
        };

    private ServiceProvider BuildProvider(DevelopmentSeedOptions? options = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ApiConfiguration.ConnectionStringKey] = Fixture.ConnectionString })
            .Build();

        // Registered before AddApplication/AddInfrastructure, whose TryAdd calls then keep these.
        return new ServiceCollection()
            .AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddProvider(_logs);
            })
            .AddSingleton<TimeProvider>(_clock)
            .AddSingleton(options ?? Curated())
            .AddApplication()
            .AddInfrastructure(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private async Task<DevelopmentSeedResult> SeedAsync(DevelopmentSeedOptions? options = null)
    {
        await using var provider = BuildProvider(options);
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDevelopmentDataSeeder>().SeedAsync(CancellationToken.None);
    }

    /// <summary>Runs the real seeder with a replaced patient factory (the internal test seam).</summary>
    private async Task<DevelopmentSeedResult> SeedWithFactoryAsync(
        Func<ValidatedFictionalPatient, DateTimeOffset, Patient> factory,
        DevelopmentSeedOptions? options = null)
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var seeder = new DevelopmentDataSeeder(
            scope.ServiceProvider.GetRequiredService<ErTriageDbContext>(),
            _clock,
            options ?? Curated(),
            scope.ServiceProvider.GetRequiredService<ILogger<DevelopmentDataSeeder>>(),
            factory);
        return await seeder.SeedAsync(CancellationToken.None);
    }

    private string WriteSeedFile(Action<JsonArray> change)
    {
        var document = JsonNode.Parse(File.ReadAllText(SeedTestData.CuratedFilePath))!.AsObject();
        change(document["patients"]!.AsArray());
        return TempFile(document.ToJsonString());
    }

    private string TempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"seed-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private static JsonObject Record(JsonArray patients, string key) =>
        patients.Select(p => p!.AsObject()).Single(p => (string?)p["seedKey"] == key);

    // Synchronous on purpose: called from inside the (synchronous) factory seam, on its own connection.
    private void InsertConflictingRow(Guid id)
    {
        using var connection = new SqlConnection(Fixture.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO Patients (Id, FullName, Age, ChiefComplaint, HeartRate, SystolicBp, DiastolicBp, TemperatureC, SpO2, " +
            "ArrivalTime, CalculatedPriority, PriorityReason, CurrentPriority, Status, CreatedAt) " +
            "VALUES (@id, N'Conflicting Row', 40, N'Conflict', 80, 120, 80, 36.8, 98, SYSUTCDATETIME(), 4, N'Test', 4, N'Waiting', SYSUTCDATETIME())";
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = id });
        command.ExecuteNonQuery();
    }

    private void RaiseSqlError()
    {
        using var connection = new SqlConnection(Fixture.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 / 0";
        command.ExecuteScalar();
    }

    private static async Task<int> GetAppLockAsync(SqlConnection connection, SqlTransaction transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', " +
            "@LockOwner = N'Transaction', @LockTimeout = 0; SELECT @r;";
        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = DevelopmentDataSeeder.LockResource });
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private async Task<int> CountAsync(string table) => await ScalarIntAsync($"SELECT COUNT(*) FROM {table}");

    private async Task<int> ScalarIntAsync(string sql, params SqlParameter[] parameters) =>
        Convert.ToInt32(await ScalarAsync(sql, parameters));

    private async Task<Guid> PatientIdForKeyAsync(string key) =>
        (Guid)(await ScalarAsync("SELECT PatientId FROM DevelopmentSeedRecords WHERE SeedKey = @key",
            new SqlParameter("@key", SqlDbType.VarChar, 50) { Value = key }))!;

    private async Task<string> RowVersionOfAsync(Guid id) =>
        Convert.ToHexString((byte[])(await ScalarAsync("SELECT RowVersion FROM Patients WHERE Id = @id", IdParameter(id)))!);

    private async Task<Dictionary<Guid, string>> RowVersionsAsync() =>
        (await ReadAsync("SELECT Id, RowVersion FROM Patients", r => (Id: r.GetGuid(0), Version: Convert.ToHexString((byte[])r[1]))))
        .ToDictionary(x => x.Id, x => x.Version);

    private static SqlParameter IdParameter(Guid id) => new("@id", SqlDbType.UniqueIdentifier) { Value = id };
}
