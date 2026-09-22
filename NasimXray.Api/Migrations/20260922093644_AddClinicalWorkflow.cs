using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NasimXray.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MeasurementFieldDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExamTypeId = table.Column<int>(type: "integer", nullable: false),
                    FieldKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DataType = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementFieldDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeasurementFieldDefinitions_ExamTypes_ExamTypeId",
                        column: x => x.ExamTypeId,
                        principalTable: "ExamTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportMeasurements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReportId = table.Column<int>(type: "integer", nullable: false),
                    FieldDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportMeasurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportMeasurements_MeasurementFieldDefinitions_FieldDefinit~",
                        column: x => x.FieldDefinitionId,
                        principalTable: "MeasurementFieldDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportMeasurements_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementFieldDefinitions_ExamTypeId_FieldKey",
                table: "MeasurementFieldDefinitions",
                columns: new[] { "ExamTypeId", "FieldKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportMeasurements_FieldDefinitionId",
                table: "ReportMeasurements",
                column: "FieldDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportMeasurements_ReportId_FieldDefinitionId",
                table: "ReportMeasurements",
                columns: new[] { "ReportId", "FieldDefinitionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportMeasurements");

            migrationBuilder.DropTable(
                name: "MeasurementFieldDefinitions");
        }
    }
}
