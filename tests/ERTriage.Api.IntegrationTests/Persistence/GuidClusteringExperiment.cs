using System.Data;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace ERTriage.Api.IntegrationTests.Persistence;

/// <summary>
/// D-38 measurement: does a time-ordered Guid v7 as clustered primary key reduce index fragmentation on SQL Server,
/// compared with a random Guid and with the database's own <c>NEWSEQUENTIALID()</c>? Opt-in (<c>ERTRIAGE_RUN_D38=1</c>),
/// runs only in the isolated test database, creates and drops its own <c>D38_*</c> tables, and asserts nothing about the
/// outcome (only that every row was inserted): it reports numbers. Rows are inserted one statement at a time, in
/// generation order, like registrations arriving over time. The result is also written to
/// <c>%TEMP%\ERTriage_D38_results.txt</c>.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class GuidClusteringExperiment
{
    private const string RowsVariable = "ERTRIAGE_D38_ROWS";
    private const int DefaultRows = 20_000;
    private const int MinRows = 1_000;
    private const int MaxRows = 200_000;
    private const int PayloadLength = 200;

    private readonly SqlServerFixture _fixture;
    private readonly ITestOutputHelper _output;

    public GuidClusteringExperiment(SqlServerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private sealed record Variant(string Table, string Label, Func<int, Guid>? NewId);

    private sealed record Result(
        string Label,
        long Rows,
        long Pages,
        double? FragmentationPercent,
        double? PageFullnessPercent,
        long Fragments,
        long LeafAllocations,
        TimeSpan Elapsed,
        double? SqlAscendingRatio);

    [D38ExperimentFact]
    public async Task Measure_FragmentationOfGuidV7_RandomGuid_AndNewSequentialId()
    {
        var rows = ReadRows();
        var start = DateTimeOffset.UtcNow;

        var variants = new[]
        {
            new Variant("dbo.D38_GuidV7", "Guid v7 (client, time-ordered)", i => Guid.CreateVersion7(start.AddMilliseconds(i * 10L))),
            new Variant("dbo.D38_RandomGuid", "Guid.NewGuid() (client, random)", _ => Guid.NewGuid()),
            new Variant("dbo.D38_NewSequentialId", "NEWSEQUENTIALID() (database)", null)
        };

        var results = new List<Result>();

        await using var connection = await _fixture.OpenConnectionAsync();

        try
        {
            foreach (var variant in variants)
            {
                results.Add(await RunVariantAsync(connection, variant, rows));
            }
        }
        finally
        {
            foreach (var variant in variants)
            {
                await ExecuteAsync(connection, $"DROP TABLE IF EXISTS {variant.Table}");
            }
        }

        Report(rows, results);

        Assert.All(results, result => Assert.Equal(rows, result.Rows));
    }

    private static int ReadRows()
    {
        var text = Environment.GetEnvironmentVariable(RowsVariable);

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rows)
            ? Math.Clamp(rows, MinRows, MaxRows)
            : DefaultRows;
    }

    private static async Task<Result> RunVariantAsync(SqlConnection connection, Variant variant, int rows)
    {
        var table = variant.Table;
        var idColumn = variant.NewId is null
            ? "Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY CLUSTERED"
            : "Id uniqueidentifier NOT NULL PRIMARY KEY CLUSTERED";

        await ExecuteAsync(
            connection,
            $"DROP TABLE IF EXISTS {table}; CREATE TABLE {table} ({idColumn}, Payload char({PayloadLength}) NOT NULL);");

        var newId = variant.NewId;
        Guid[]? ids = newId is null ? null : Enumerable.Range(0, rows).Select(newId).ToArray();

        var stopwatch = Stopwatch.StartNew();
        await InsertRowsAsync(connection, table, ids, rows);
        stopwatch.Stop();

        return await MeasureAsync(connection, variant.Label, table, stopwatch.Elapsed, ids is null ? null : AscendingRatio(ids));
    }

    private static async Task InsertRowsAsync(SqlConnection connection, string table, Guid[]? ids, int rows)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ids is null
            ? $"INSERT INTO {table} (Payload) VALUES (@payload)"
            : $"INSERT INTO {table} (Id, Payload) VALUES (@id, @payload)";

        command.Parameters.Add("@payload", SqlDbType.Char, PayloadLength).Value = new string('x', PayloadLength);
        var idParameter = ids is null ? null : command.Parameters.Add("@id", SqlDbType.UniqueIdentifier);

        for (var i = 0; i < rows; i++)
        {
            if (idParameter is not null && ids is not null)
            {
                idParameter.Value = ids[i];
            }

            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static async Task<Result> MeasureAsync(
        SqlConnection connection,
        string label,
        string table,
        TimeSpan elapsed,
        double? ascendingRatio)
    {
        await using var count = connection.CreateCommand();
        count.CommandText = $"SELECT COUNT_BIG(*) FROM {table}";
        var rows = Convert.ToInt64(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT ps.page_count, ps.avg_fragmentation_in_percent, ps.avg_page_space_used_in_percent, ps.fragment_count, " +
            "(SELECT os.leaf_allocation_count FROM sys.dm_db_index_operational_stats(DB_ID(), OBJECT_ID(@table), 1, NULL) os) " +
            "FROM sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID(@table), 1, NULL, 'DETAILED') ps " +
            "WHERE ps.index_level = 0";
        command.Parameters.Add("@table", SqlDbType.NVarChar, 128).Value = table;

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"No index statistics were returned for {table}.");
        }

        return new Result(
            label,
            rows,
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetDouble(1),
            reader.IsDBNull(2) ? null : reader.GetDouble(2),
            reader.GetInt64(3),
            reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
            elapsed,
            ascendingRatio);
    }

    /// <summary>
    /// Share of consecutive Guids that SQL Server itself orders ascending (SqlGuid comparison). 1.0 means the order of
    /// creation equals the index order; about 0.5 means it behaves like random keys.
    /// </summary>
    private static double AscendingRatio(Guid[] ids)
    {
        var ascending = 0;

        for (var i = 1; i < ids.Length; i++)
        {
            if (new SqlGuid(ids[i]).CompareTo(new SqlGuid(ids[i - 1])) > 0)
            {
                ascending++;
            }
        }

        return ascending / (double)(ids.Length - 1);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private void Report(int rows, IReadOnlyList<Result> results)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"D-38 clustered-index measurement: {rows} single-row inserts per variant, row payload {PayloadLength} chars");
        text.AppendLine("Lower fragmentation, fewer pages and fewer leaf allocations are better; higher page fullness is better.");
        text.AppendLine();
        text.AppendLine("Variant | Rows | Pages | Fragmentation % | Page fullness % | Fragments | Leaf allocations | SqlGuid ascending | Insert time");

        foreach (var r in results)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"{r.Label} | {r.Rows} | {r.Pages} | {Format(r.FragmentationPercent)} | {Format(r.PageFullnessPercent)} | {r.Fragments} | {r.LeafAllocations} | {Format(r.SqlAscendingRatio)} | {r.Elapsed.TotalSeconds:0.0}s");
        }

        var report = text.ToString();
        _output.WriteLine(report);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "ERTriage_D38_results.txt"), report);
    }

    private static string Format(double? value) =>
        value is null ? "n/a" : value.Value.ToString("0.00", CultureInfo.InvariantCulture);
}
