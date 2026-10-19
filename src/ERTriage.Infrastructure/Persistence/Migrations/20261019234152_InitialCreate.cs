using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERTriage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Patients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Age = table.Column<byte>(type: "tinyint", nullable: false),
                    ChiefComplaint = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HeartRate = table.Column<short>(type: "smallint", nullable: false),
                    SystolicBp = table.Column<short>(type: "smallint", nullable: false),
                    DiastolicBp = table.Column<short>(type: "smallint", nullable: false),
                    TemperatureC = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: false),
                    SpO2 = table.Column<byte>(type: "tinyint", nullable: false),
                    ArrivalTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CalculatedPriority = table.Column<byte>(type: "tinyint", nullable: false),
                    PriorityReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OverridePriority = table.Column<byte>(type: "tinyint", nullable: true),
                    OverrideReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OverriddenBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OverriddenAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentPriority = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TreatmentStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DispositionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patients", x => x.Id);
                    table.CheckConstraint("CK_Patients_Age", "[Age] BETWEEN 0 AND 120");
                    table.CheckConstraint("CK_Patients_CalculatedPriority", "[CalculatedPriority] BETWEEN 1 AND 4");
                    table.CheckConstraint("CK_Patients_CurrentPriority", "[CurrentPriority] = COALESCE([OverridePriority], [CalculatedPriority])");
                    table.CheckConstraint("CK_Patients_DiastolicBelowSystolic", "[DiastolicBp] < [SystolicBp]");
                    table.CheckConstraint("CK_Patients_DiastolicBp", "[DiastolicBp] BETWEEN 20 AND 200");
                    table.CheckConstraint("CK_Patients_HeartRate", "[HeartRate] BETWEEN 20 AND 250");
                    table.CheckConstraint("CK_Patients_OverrideFieldsConsistent", "([OverridePriority] IS NULL AND [OverrideReason] IS NULL AND [OverriddenAt] IS NULL) OR ([OverridePriority] IS NOT NULL AND [OverrideReason] IS NOT NULL AND [OverriddenAt] IS NOT NULL)");
                    table.CheckConstraint("CK_Patients_OverridePriority", "[OverridePriority] BETWEEN 1 AND 4");
                    table.CheckConstraint("CK_Patients_SpO2", "[SpO2] BETWEEN 50 AND 100");
                    table.CheckConstraint("CK_Patients_Status", "[Status] IN ('Waiting', 'InTreatment', 'Discharged', 'Admitted')");
                    table.CheckConstraint("CK_Patients_SystolicBp", "[SystolicBp] BETWEEN 50 AND 300");
                    table.CheckConstraint("CK_Patients_TemperatureC", "[TemperatureC] BETWEEN 30.0 AND 45.0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_Status_CurrentPriority_ArrivalTime_Id",
                table: "Patients",
                columns: new[] { "Status", "CurrentPriority", "ArrivalTime", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Patients");
        }
    }
}
