using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERTriage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDevelopmentSeedRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DevelopmentSeedRecords",
                columns: table => new
                {
                    SeedKey = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeededAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevelopmentSeedRecords", x => x.SeedKey);
                    table.ForeignKey(
                        name: "FK_DevelopmentSeedRecords_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_DevelopmentSeedRecords_PatientId",
                table: "DevelopmentSeedRecords",
                column: "PatientId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DevelopmentSeedRecords");
        }
    }
}
