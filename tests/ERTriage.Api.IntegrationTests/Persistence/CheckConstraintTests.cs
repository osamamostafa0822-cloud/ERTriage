using System.Data;
using Microsoft.Data.SqlClient;

namespace ERTriage.Api.IntegrationTests.Persistence;

/// <summary>
/// The 12 CHECK constraints against real SQL Server. The Domain would reject these values first, so rows are inserted
/// with raw SQL: this proves the database is an independent safety net. Every invalid case changes exactly one rule, so
/// the violated constraint name in the error identifies the rule.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class CheckConstraintTests(SqlServerFixture fixture) : DatabaseTestBase(fixture)
{
    private const int CheckViolation = 547;
    private const int ArithmeticOverflow = 220;

    public static readonly string[] ConstraintNames =
    [
        "CK_Patients_Age", "CK_Patients_CalculatedPriority", "CK_Patients_CurrentPriority",
        "CK_Patients_DiastolicBelowSystolic", "CK_Patients_DiastolicBp", "CK_Patients_HeartRate",
        "CK_Patients_OverrideFieldsConsistent", "CK_Patients_OverridePriority", "CK_Patients_SpO2",
        "CK_Patients_Status", "CK_Patients_SystolicBp", "CK_Patients_TemperatureC"
    ];

    private static readonly (string Name, SqlDbType Type)[] Columns =
    [
        ("Id", SqlDbType.UniqueIdentifier), ("FullName", SqlDbType.NVarChar), ("Age", SqlDbType.Int),
        ("ChiefComplaint", SqlDbType.NVarChar), ("HeartRate", SqlDbType.Int), ("SystolicBp", SqlDbType.Int),
        ("DiastolicBp", SqlDbType.Int), ("TemperatureC", SqlDbType.Decimal), ("SpO2", SqlDbType.Int),
        ("ArrivalTime", SqlDbType.DateTime2), ("CalculatedPriority", SqlDbType.Int), ("PriorityReason", SqlDbType.NVarChar),
        ("OverridePriority", SqlDbType.Int), ("OverrideReason", SqlDbType.NVarChar), ("OverriddenBy", SqlDbType.NVarChar),
        ("OverriddenAt", SqlDbType.DateTime2), ("CurrentPriority", SqlDbType.Int), ("Status", SqlDbType.NVarChar),
        ("TreatmentStartedAt", SqlDbType.DateTime2), ("CompletedAt", SqlDbType.DateTime2),
        ("DispositionNote", SqlDbType.NVarChar), ("CreatedAt", SqlDbType.DateTime2)
    ];

    // Key format: "<constraint>:<case>". Each case starts from a fully valid row and changes only what it names.
    private static readonly Dictionary<string, Action<Dictionary<string, object?>>> Valid = new()
    {
        ["CK_Patients_Age:0"] = r => r["Age"] = 0,
        ["CK_Patients_Age:120"] = r => r["Age"] = 120,
        ["CK_Patients_HeartRate:20"] = r => r["HeartRate"] = 20,
        ["CK_Patients_HeartRate:250"] = r => r["HeartRate"] = 250,
        ["CK_Patients_SystolicBp:50"] = r => { r["SystolicBp"] = 50; r["DiastolicBp"] = 40; },
        ["CK_Patients_SystolicBp:300"] = r => r["SystolicBp"] = 300,
        ["CK_Patients_DiastolicBp:20"] = r => r["DiastolicBp"] = 20,
        ["CK_Patients_DiastolicBp:200"] = r => { r["SystolicBp"] = 250; r["DiastolicBp"] = 200; },
        ["CK_Patients_TemperatureC:30.0"] = r => r["TemperatureC"] = 30.0m,
        ["CK_Patients_TemperatureC:45.0"] = r => r["TemperatureC"] = 45.0m,
        ["CK_Patients_SpO2:50"] = r => r["SpO2"] = 50,
        ["CK_Patients_SpO2:100"] = r => r["SpO2"] = 100,
        ["CK_Patients_DiastolicBelowSystolic:one-below"] = r => { r["SystolicBp"] = 120; r["DiastolicBp"] = 119; },
        ["CK_Patients_CalculatedPriority:1"] = r => { r["CalculatedPriority"] = 1; r["CurrentPriority"] = 1; },
        ["CK_Patients_CalculatedPriority:4"] = r => { r["CalculatedPriority"] = 4; r["CurrentPriority"] = 4; },
        ["CK_Patients_OverridePriority:1"] = r => SetOverride(r, 1),
        ["CK_Patients_OverridePriority:4"] = r => SetOverride(r, 4),
        ["CK_Patients_OverrideFieldsConsistent:no-override"] = _ => { },
        ["CK_Patients_OverrideFieldsConsistent:complete-override"] = r => { SetOverride(r, 3); r["OverriddenBy"] = "Nurse Test"; },
        ["CK_Patients_CurrentPriority:override-wins"] = r => { r["CalculatedPriority"] = 3; SetOverride(r, 1); },
        ["CK_Patients_Status:Waiting"] = r => r["Status"] = "Waiting",
        ["CK_Patients_Status:InTreatment"] = r => r["Status"] = "InTreatment",
        ["CK_Patients_Status:Discharged"] = r => r["Status"] = "Discharged",
        ["CK_Patients_Status:Admitted"] = r => r["Status"] = "Admitted"
    };

    private static readonly Dictionary<string, Action<Dictionary<string, object?>>> Invalid = new()
    {
        ["CK_Patients_Age:121"] = r => r["Age"] = 121,
        ["CK_Patients_HeartRate:19"] = r => r["HeartRate"] = 19,
        ["CK_Patients_HeartRate:251"] = r => r["HeartRate"] = 251,
        ["CK_Patients_SystolicBp:49"] = r => { r["SystolicBp"] = 49; r["DiastolicBp"] = 40; },
        ["CK_Patients_SystolicBp:301"] = r => r["SystolicBp"] = 301,
        ["CK_Patients_DiastolicBp:19"] = r => r["DiastolicBp"] = 19,
        ["CK_Patients_DiastolicBp:201"] = r => { r["SystolicBp"] = 250; r["DiastolicBp"] = 201; },
        ["CK_Patients_TemperatureC:29.9"] = r => r["TemperatureC"] = 29.9m,
        ["CK_Patients_TemperatureC:45.1"] = r => r["TemperatureC"] = 45.1m,
        ["CK_Patients_SpO2:49"] = r => r["SpO2"] = 49,
        ["CK_Patients_SpO2:101"] = r => r["SpO2"] = 101,
        ["CK_Patients_DiastolicBelowSystolic:equal"] = r => { r["SystolicBp"] = 100; r["DiastolicBp"] = 100; },
        ["CK_Patients_DiastolicBelowSystolic:above"] = r => { r["SystolicBp"] = 100; r["DiastolicBp"] = 110; },
        ["CK_Patients_CalculatedPriority:0"] = r => { r["CalculatedPriority"] = 0; r["CurrentPriority"] = 0; },
        ["CK_Patients_CalculatedPriority:5"] = r => { r["CalculatedPriority"] = 5; r["CurrentPriority"] = 5; },
        ["CK_Patients_OverridePriority:0"] = r => SetOverride(r, 0),
        ["CK_Patients_OverridePriority:5"] = r => SetOverride(r, 5),
        ["CK_Patients_OverrideFieldsConsistent:priority-without-reason"] = r => { SetOverride(r, 2); r["OverrideReason"] = null; },
        ["CK_Patients_OverrideFieldsConsistent:priority-without-timestamp"] = r => { SetOverride(r, 2); r["OverriddenAt"] = null; },
        ["CK_Patients_OverrideFieldsConsistent:reason-without-priority"] = r => r["OverrideReason"] = "Orphan reason",
        ["CK_Patients_OverrideFieldsConsistent:timestamp-without-priority"] = r => r["OverriddenAt"] = Now(),
        ["CK_Patients_CurrentPriority:no-override-mismatch"] = r => r["CurrentPriority"] = 3,
        ["CK_Patients_CurrentPriority:override-ignored"] = r => { SetOverride(r, 1); r["CurrentPriority"] = 2; },
        ["CK_Patients_Status:Unknown"] = r => r["Status"] = "Unknown",
        ["CK_Patients_Status:empty"] = r => r["Status"] = string.Empty
    };

    public static TheoryData<string> ValidCases => ToTheoryData(Valid.Keys);

    public static TheoryData<string> InvalidCases => ToTheoryData(Invalid.Keys);

    /// <summary>Constraint names that have at least one valid and one invalid case (used by the coverage test).</summary>
    internal static (IReadOnlySet<string> Valid, IReadOnlySet<string> Invalid) CoveredConstraints() =>
        (Valid.Keys.Select(ConstraintOf).ToHashSet(), Invalid.Keys.Select(ConstraintOf).ToHashSet());

    [SqlServerTheory]
    [MemberData(nameof(ValidCases))]
    public async Task ValidBoundaryValue_IsAccepted(string caseName)
    {
        var row = Baseline();
        Valid[caseName](row);

        await InsertAsync(row);

        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM Patients"));
    }

    [SqlServerTheory]
    [MemberData(nameof(InvalidCases))]
    public async Task InvalidValue_IsRejectedByTheNamedCheckConstraint(string caseName)
    {
        var row = Baseline();
        Invalid[caseName](row);

        var exception = await Assert.ThrowsAsync<SqlException>(() => InsertAsync(row));

        Assert.Equal(CheckViolation, exception.Number);
        Assert.Contains($"\"{ConstraintOf(caseName)}\"", exception.Message);
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM Patients"));
    }

    [SqlServerFact]
    public async Task Age_BelowZero_IsRejectedByTheTinyintColumnTypeItself()
    {
        // tinyint cannot hold -1, so the lower bound of CK_Patients_Age can never be reached through SQL Server:
        // the type rejects it first (error 220, not 547). The Domain still rejects negative ages before this point.
        var row = Baseline();
        row["Age"] = -1;

        var exception = await Assert.ThrowsAsync<SqlException>(() => InsertAsync(row));

        Assert.Equal(ArithmeticOverflow, exception.Number);
    }

    private static string ConstraintOf(string caseName) => caseName[..caseName.IndexOf(':')];

    private static DateTime Now() => PersistenceTestBase.Now.UtcDateTime;

    private static Dictionary<string, object?> Baseline() => new()
    {
        ["Id"] = Guid.NewGuid(),
        ["FullName"] = "Check Patient",
        ["Age"] = 40,
        ["ChiefComplaint"] = "Chest pain",
        ["HeartRate"] = 80,
        ["SystolicBp"] = 120,
        ["DiastolicBp"] = 80,
        ["TemperatureC"] = 36.8m,
        ["SpO2"] = 98,
        ["ArrivalTime"] = Now(),
        ["CalculatedPriority"] = 2,
        ["PriorityReason"] = "Test reason",
        ["OverridePriority"] = null,
        ["OverrideReason"] = null,
        ["OverriddenBy"] = null,
        ["OverriddenAt"] = null,
        ["CurrentPriority"] = 2,
        ["Status"] = "Waiting",
        ["TreatmentStartedAt"] = null,
        ["CompletedAt"] = null,
        ["DispositionNote"] = null,
        ["CreatedAt"] = Now()
    };

    // A consistent override: priority, reason and timestamp together, and CurrentPriority following the override.
    private static void SetOverride(Dictionary<string, object?> row, int priority)
    {
        row["OverridePriority"] = priority;
        row["OverrideReason"] = "Escalated by nurse";
        row["OverriddenAt"] = Now();
        row["CurrentPriority"] = priority;
    }

    private static TheoryData<string> ToTheoryData(IEnumerable<string> keys)
    {
        var data = new TheoryData<string>();

        foreach (var key in keys)
        {
            data.Add(key);
        }

        return data;
    }

    private async Task InsertAsync(Dictionary<string, object?> row)
    {
        await using var connection = await Fixture.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"INSERT INTO Patients ({string.Join(", ", Columns.Select(c => c.Name))}) " +
            $"VALUES ({string.Join(", ", Columns.Select(c => "@" + c.Name))})";

        foreach (var (name, type) in Columns)
        {
            var parameter = new SqlParameter("@" + name, type) { Value = row[name] ?? DBNull.Value };

            if (type == SqlDbType.Decimal)
            {
                parameter.Precision = 4;
                parameter.Scale = 1;
            }

            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>Needs no database: guards that every one of the 12 constraints has a valid and an invalid test case.</summary>
public class CheckConstraintCoverageTests
{
    [Fact]
    public void EveryConstraint_HasAtLeastOneValidAndOneInvalidCase()
    {
        var (valid, invalid) = CheckConstraintTests.CoveredConstraints();
        var expected = CheckConstraintTests.ConstraintNames.ToHashSet();

        Assert.Equal(12, expected.Count);
        Assert.True(expected.SetEquals(valid), "Every constraint needs a valid boundary case.");
        Assert.True(expected.SetEquals(invalid), "Every constraint needs an invalid case.");
    }
}
