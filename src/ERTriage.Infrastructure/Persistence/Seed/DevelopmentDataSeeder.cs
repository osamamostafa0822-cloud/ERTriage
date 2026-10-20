using System.Data;
using System.Data.Common;
using ERTriage.Application.Common;
using ERTriage.Domain.Common;
using ERTriage.Domain.Patients;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace ERTriage.Infrastructure.Persistence.Seed;

/// <summary>
/// Best-effort Development seed (D-44, D-58, D-62).
/// <list type="number">
/// <item>Load the JSON; an unusable document → <see cref="DevelopmentSeedOutcome.Failed"/>, nothing written. Invalid records
/// are reported and skipped; valid ones continue.</item>
/// <item>One transaction holds a transaction-owned <c>sp_getapplock</c> for the whole run (return code checked), so
/// concurrent starts are serialised and cannot insert a key twice.</item>
/// <item>Every missing patient is created through the Domain first (a Domain rule violation affects only that record).
/// Patients are then saved in batches (<see cref="DevelopmentSeedOptions.BatchSize"/>), each behind a savepoint; a
/// recoverable constraint error rolls the batch back and retries it record by record, each with its own savepoint, so
/// <b>only the failing record</b> is lost (no partial patient or marker); it is retried on the next start.</item>
/// <item>Anything else (connection, timeout, deadlock, transaction failure, unexpected exception) is fatal: logged as an
/// Error with the exception and rethrown; the transaction rolls back.</item>
/// </list>
/// Existing rows are never updated or deleted. Logs carry codes, field names, indexes and seed keys only.
/// </summary>
internal sealed class DevelopmentDataSeeder : IDevelopmentDataSeeder
{
    internal const string LockResource = "ERTriage.DevelopmentSeed";

    // sp_getapplock return codes (SQL Server documentation).
    private const int LockGranted = 0;
    private const int LockGrantedAfterWait = 1;
    private const int LockTimeout = -1;

    // Row-level, statement-terminating SQL Server errors: the transaction stays usable and only the record is affected.
    // 2627/2601 duplicate key, 547 FK/CHECK, 515 NULL, 8152/2628 truncation, 220/8115 arithmetic overflow.
    private static readonly HashSet<int> RecoverableSqlErrors = [2627, 2601, 547, 515, 8152, 2628, 220, 8115];

    private readonly ErTriageDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly DevelopmentSeedOptions _options;
    private readonly ILogger<DevelopmentDataSeeder> _logger;
    private readonly Func<ValidatedFictionalPatient, DateTimeOffset, Patient> _createPatient;

    public DevelopmentDataSeeder(
        ErTriageDbContext context,
        TimeProvider timeProvider,
        DevelopmentSeedOptions options,
        ILogger<DevelopmentDataSeeder> logger)
        : this(context, timeProvider, options, logger, FictionalPatientFactory.Create)
    {
    }

    /// <summary>Test seam: lets tests make one record fail in the Domain or the database. Not used by DI (internal).</summary>
    internal DevelopmentDataSeeder(
        ErTriageDbContext context,
        TimeProvider timeProvider,
        DevelopmentSeedOptions options,
        ILogger<DevelopmentDataSeeder> logger,
        Func<ValidatedFictionalPatient, DateTimeOffset, Patient> createPatient)
    {
        _context = context;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
        _createPatient = createPatient;
    }

    public async Task<DevelopmentSeedResult> SeedAsync(CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();

        SeedLoadResult load;
        try
        {
            load = await FictionalPatientSeedLoader.LoadAsync(_options.DataFilePath, cancellationToken);
        }
        catch (SeedDataException ex)
        {
            _logger.LogError(
                "Development seed failed: the seed document is unusable ({ErrorCode}); nothing was written. SeedRunId {SeedRunId}",
                ex.Code,
                runId);
            return new DevelopmentSeedResult(runId, DevelopmentSeedOutcome.Failed, 0, 0, 0, 0, 0, DocumentErrorCode: ex.Code);
        }

        foreach (var issue in load.Issues)
        {
            _logger.LogWarning(
                "Development seed skipped record {RecordIndex} (SeedKey {SeedKey}): {ErrorCode} on {FieldName}. SeedRunId {SeedRunId}",
                issue.Index,
                issue.SeedKey ?? "(invalid)",
                issue.Code,
                issue.Field,
                runId);
        }

        if (load.Valid.Count == 0)
        {
            // Record-level failure of every record: no database work, no lock (DocumentErrorCode stays null).
            LogSummary(runId, DevelopmentSeedOutcome.Failed, load, 0, 0, 0);
            return new DevelopmentSeedResult(runId, DevelopmentSeedOutcome.Failed, load.TotalRecords, 0, 0, load.SkippedCount, 0);
        }

        try
        {
            return await SeedValidRecordsAsync(runId, load, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fatal: never skipped silently. Logged once here and rethrown unchanged; the transaction rolls back.
            // Cancellation is not a failure: it propagates unlogged (the host is stopping) and also rolls back.
            _logger.LogError(ex, "Development seed failed fatally; this run wrote nothing. SeedRunId {SeedRunId}", runId);
            throw;
        }
    }

    private async Task<DevelopmentSeedResult> SeedValidRecordsAsync(Guid runId, SeedLoadResult load, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var lockResult = await AcquireLockAsync(transaction, cancellationToken);

        if (lockResult == LockTimeout)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(
                "Development seed skipped: the seed lock was not acquired within {LockTimeoutSeconds} s (another instance is seeding). SeedRunId {SeedRunId}",
                _options.LockTimeout.TotalSeconds,
                runId);
            return new DevelopmentSeedResult(runId, DevelopmentSeedOutcome.LockNotAcquired, load.TotalRecords, 0, 0, load.SkippedCount, 0);
        }

        if (lockResult is not (LockGranted or LockGrantedAfterWait))
        {
            // -2 cancelled, -3 deadlock victim, -999 parameter error: never continue without the lock.
            throw new InvalidOperationException($"Development seed could not acquire its lock (sp_getapplock returned {lockResult}).");
        }

        var existingKeys = (await _context.DevelopmentSeedRecords
                .AsNoTracking()
                .Select(r => r.SeedKey)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        int inserted = 0, alreadyExisting = 0, failed = 0;

        // 1. Create every missing patient through the Domain. A DomainException affects only that record.
        var pending = new List<(ValidatedFictionalPatient Record, Patient Patient)>();
        foreach (var record in load.Valid)
        {
            if (existingKeys.Contains(record.SeedKey))
            {
                alreadyExisting++;
                continue;
            }

            try
            {
                pending.Add((record, _createPatient(record, now)));
            }
            catch (DomainException ex)
            {
                LogRecordFailure(runId, record, SeedErrorCodes.DomainRuleViolation, "domain", ex.GetType().Name, null);
                failed++;
            }
        }

        // 2. Save in batches, each behind a savepoint. A recoverable error rolls the batch back and retries it record by
        //    record, so only the failing record is lost.
        var batchSize = Math.Max(1, _options.BatchSize);
        var batchNumber = 0;
        foreach (var batch in pending.Chunk(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (batch.Length > 1 && await TrySaveBatchAsync(transaction, batch, ++batchNumber, now, cancellationToken))
            {
                inserted += batch.Length;
                continue;
            }

            foreach (var (record, patient) in batch)
            {
                switch (await SeedRecordAsync(runId, transaction, record, patient, now, cancellationToken))
                {
                    case RecordOutcome.Inserted:
                        inserted++;
                        break;
                    case RecordOutcome.AlreadyExisting:
                        alreadyExisting++;
                        break;
                    default:
                        failed++;
                        break;
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);

        var outcome = Classify(load, inserted, alreadyExisting, failed);
        LogSummary(runId, outcome, load, inserted, alreadyExisting, failed);

        return new DevelopmentSeedResult(runId, outcome, load.TotalRecords, inserted, alreadyExisting, load.SkippedCount, failed);
    }

    /// <summary>
    /// Saves a whole batch behind one savepoint. Returns false (after rolling the batch back and clearing the change
    /// tracker) when a recoverable error occurs, so the caller retries the batch record by record. Fatal errors propagate.
    /// </summary>
    private async Task<bool> TrySaveBatchAsync(
        IDbContextTransaction transaction,
        (ValidatedFictionalPatient Record, Patient Patient)[] batch,
        int batchNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var savepoint = $"seed_batch_{batchNumber}";
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);

        foreach (var (record, patient) in batch)
        {
            _context.Patients.Add(patient);
            _context.DevelopmentSeedRecords.Add(new DevelopmentSeedRecord(record.SeedKey, patient.Id, now.UtcDateTime));
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (RecoverableSqlNumber(ex) is not null)
        {
            await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
            return false;
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    private async Task<RecordOutcome> SeedRecordAsync(
        Guid runId,
        IDbContextTransaction transaction,
        ValidatedFictionalPatient record,
        Patient patient,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var savepoint = $"seed_{record.Index}";
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);

        _context.Patients.Add(patient);
        _context.DevelopmentSeedRecords.Add(new DevelopmentSeedRecord(record.SeedKey, patient.Id, now.UtcDateTime));

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return RecordOutcome.Inserted;
        }
        catch (DbUpdateException ex) when (RecoverableSqlNumber(ex) is { } sqlNumber)
        {
            // Undo this record only (patient and marker together), forget its tracked entities, keep going.
            await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
            _context.ChangeTracker.Clear();

            if (await _context.DevelopmentSeedRecords.AsNoTracking().AnyAsync(r => r.SeedKey == record.SeedKey, cancellationToken))
            {
                return RecordOutcome.AlreadyExisting; // inserted concurrently by someone else; not a failure
            }

            LogRecordFailure(runId, record, SeedErrorCodes.DatabaseConstraint, "database", ex.GetType().Name, sqlNumber);
            return RecordOutcome.Failed;
        }
        finally
        {
            // Saved entries are not needed again; failed ones must not be retried by a later SaveChanges.
            _context.ChangeTracker.Clear();
        }
    }

    /// <summary>The single summary entry of a run (Warning when the demo data is incomplete).</summary>
    private void LogSummary(Guid runId, DevelopmentSeedOutcome outcome, SeedLoadResult load, int inserted, int alreadyExisting, int failed) =>
        _logger.Log(
            outcome is DevelopmentSeedOutcome.PartiallyCompleted or DevelopmentSeedOutcome.Failed ? LogLevel.Warning : LogLevel.Information,
            "Development seed {Outcome}: TotalRecords {TotalRecords}, InsertedCount {InsertedCount}, AlreadyExistingCount {AlreadyExistingCount}, SkippedCount {SkippedCount}, FailedCount {FailedCount}. SeedRunId {SeedRunId}",
            outcome,
            load.TotalRecords,
            inserted,
            alreadyExisting,
            load.SkippedCount,
            failed,
            runId);

    private void LogRecordFailure(Guid runId, ValidatedFictionalPatient record, string code, string field, string exceptionType, int? sqlNumber) =>
        _logger.LogWarning(
            "Development seed failed record {RecordIndex} (SeedKey {SeedKey}): {ErrorCode} on {FieldName} ({ExceptionType}, SqlError {SqlErrorNumber}); only this record was rolled back. SeedRunId {SeedRunId}",
            record.Index,
            record.SeedKey,
            code,
            field,
            exceptionType,
            sqlNumber,
            runId);

    private static DevelopmentSeedOutcome Classify(SeedLoadResult load, int inserted, int alreadyExisting, int failed)
    {
        if (inserted + alreadyExisting == 0)
        {
            return DevelopmentSeedOutcome.Failed; // nothing of the demo could be seeded
        }

        if (load.SkippedCount > 0 || failed > 0)
        {
            return DevelopmentSeedOutcome.PartiallyCompleted;
        }

        return inserted > 0 ? DevelopmentSeedOutcome.Completed : DevelopmentSeedOutcome.AlreadyComplete;
    }

    private async Task<int> AcquireLockAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();

        // Transaction-owned: released automatically on commit, rollback or a dropped connection.
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            "DECLARE @result int; " +
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = @timeoutMs; " +
            "SELECT @result;";

        var timeoutMs = (int)Math.Clamp(_options.LockTimeout.TotalMilliseconds, 0, int.MaxValue);
        command.CommandTimeout = (int)Math.Ceiling(_options.LockTimeout.TotalSeconds) + 15;
        AddParameter(command, "@resource", DbType.String, LockResource);
        AddParameter(command, "@timeoutMs", DbType.Int32, timeoutMs);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static int? RecoverableSqlNumber(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && RecoverableSqlErrors.Contains(sql.Number) ? sql.Number : null;

    private enum RecordOutcome
    {
        Inserted,
        AlreadyExisting,
        Failed
    }
}
