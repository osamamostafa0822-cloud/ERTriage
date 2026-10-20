using ERTriage.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERTriage.Api.IntegrationTests.Persistence;

/// <summary>What the real migration created in a real SQL Server database (types, order, keys, indexes, constraints).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SchemaTests(SqlServerFixture fixture) : PersistenceTestBase(fixture)
{
    private const string BoardIndex = "IX_Patients_Status_CurrentPriority_ArrivalTime_Id";

    private static readonly string[] ExpectedColumns =
    [
        "Id uniqueidentifier NOT NULL",
        "FullName nvarchar(100) NOT NULL",
        "Age tinyint NOT NULL",
        "ChiefComplaint nvarchar(200) NOT NULL",
        "HeartRate smallint NOT NULL",
        "SystolicBp smallint NOT NULL",
        "DiastolicBp smallint NOT NULL",
        "TemperatureC decimal(4,1) NOT NULL",
        "SpO2 tinyint NOT NULL",
        "ArrivalTime datetime2(7) NOT NULL",
        "CalculatedPriority tinyint NOT NULL",
        "PriorityReason nvarchar(200) NOT NULL",
        "OverridePriority tinyint NULL",
        "OverrideReason nvarchar(500) NULL",
        "OverriddenBy nvarchar(100) NULL",
        "OverriddenAt datetime2(7) NULL",
        "CurrentPriority tinyint NOT NULL",
        "Status nvarchar(20) NOT NULL",
        "TreatmentStartedAt datetime2(7) NULL",
        "CompletedAt datetime2(7) NULL",
        "DispositionNote nvarchar(500) NULL",
        "CreatedAt datetime2(7) NOT NULL",
        // rowversion is reported as "timestamp" by INFORMATION_SCHEMA.
        "RowVersion timestamp NOT NULL"
    ];

    [SqlServerFact]
    public async Task Columns_AreCreatedWithTheApprovedTypesNullabilityAndOrder()
    {
        var actual = await ReadAsync(
            "SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, DATETIME_PRECISION, IS_NULLABLE " +
            "FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Patients' ORDER BY ORDINAL_POSITION",
            Describe);

        Assert.Equal(ExpectedColumns, actual);
    }

    private static string Describe(SqlDataReader reader)
    {
        var type = reader.GetString(1);
        var description = type switch
        {
            "nvarchar" => $"nvarchar({Convert.ToInt32(reader.GetValue(2))})",
            "decimal" => $"decimal({Convert.ToInt32(reader.GetValue(3))},{Convert.ToInt32(reader.GetValue(4))})",
            "datetime2" => $"datetime2({Convert.ToInt32(reader.GetValue(5))})",
            _ => type
        };

        return $"{reader.GetString(0)} {description} {(reader.GetString(6) == "YES" ? "NULL" : "NOT NULL")}";
    }

    [SqlServerFact]
    public async Task Id_IsNotAnIdentityColumn_BecauseTheDomainAssignsIt()
    {
        var isIdentity = await ScalarAsync(
            "SELECT is_identity FROM sys.columns WHERE object_id = OBJECT_ID(N'Patients') AND name = N'Id'");

        Assert.Equal(false, isIdentity);
    }

    [SqlServerFact]
    public async Task PrimaryKey_IsClusteredAndTheBoardIndexIsNonClustered()
    {
        var indexes = await ReadAsync(
            "SELECT name, type_desc, is_primary_key, is_unique FROM sys.indexes " +
            "WHERE object_id = OBJECT_ID(N'Patients') AND type > 0 ORDER BY index_id",
            r => $"{r.GetString(0)}|{r.GetString(1)}|{r.GetBoolean(2)}|{r.GetBoolean(3)}");

        Assert.Equal(
            [
                "PK_Patients|CLUSTERED|True|True",
                $"{BoardIndex}|NONCLUSTERED|False|False"
            ],
            indexes);
    }

    [SqlServerTheory]
    [InlineData("PK_Patients", "Id")]
    [InlineData(BoardIndex, "Status,CurrentPriority,ArrivalTime,Id")]
    public async Task IndexKeys_AreInTheApprovedOrderAndAscending(string indexName, string expectedColumns)
    {
        var keys = await ReadAsync(
            "SELECT c.name, ic.is_descending_key FROM sys.index_columns ic " +
            "JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id " +
            "JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id " +
            "WHERE ic.object_id = OBJECT_ID(N'Patients') AND i.name = @name AND ic.is_included_column = 0 " +
            "ORDER BY ic.key_ordinal",
            r => (Column: r.GetString(0), Descending: r.GetBoolean(1)),
            new SqlParameter("@name", indexName));

        Assert.Equal(expectedColumns, string.Join(",", keys.Select(k => k.Column)));
        Assert.All(keys, k => Assert.False(k.Descending));
    }

    [SqlServerFact]
    public async Task CheckConstraints_AreTheExpectedTwelve_EnabledAndTrusted()
    {
        var constraints = await ReadAsync(
            "SELECT name, is_disabled, is_not_trusted FROM sys.check_constraints " +
            "WHERE parent_object_id = OBJECT_ID(N'Patients') ORDER BY name",
            r => (Name: r.GetString(0), Disabled: r.GetBoolean(1), NotTrusted: r.GetBoolean(2)));

        Assert.Equal(CheckConstraintTests.ConstraintNames.Order(StringComparer.Ordinal), constraints.Select(c => c.Name));
        Assert.All(constraints, c =>
        {
            Assert.False(c.Disabled);
            Assert.False(c.NotTrusted);
        });
    }

    [SqlServerFact]
    public async Task MigrationHistory_RecordsInitialCreate_AndNothingIsPending()
    {
        var applied = await ReadAsync("SELECT MigrationId FROM __EFMigrationsHistory", r => r.GetString(0));

        Assert.Contains(applied, id => id.EndsWith("_InitialCreate", StringComparison.Ordinal));

        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ErTriageDbContext>();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }
}
