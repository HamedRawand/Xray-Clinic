using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NasimXray.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExamRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExamOrders_ExamTypeId",
                table: "ExamOrders");

            migrationBuilder.AddColumn<string>(
                name: "ExamNumber",
                table: "ExamOrders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Give every pre-existing exam a number before the unique index is built.
            migrationBuilder.Sql("UPDATE \"ExamOrders\" SET \"ExamNumber\" = 'E-' || lpad(\"Id\"::text, 6, '0') WHERE \"ExamNumber\" IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_ExamOrders_CreatedAt",
                table: "ExamOrders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExamOrders_ExamNumber",
                table: "ExamOrders",
                column: "ExamNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamOrders_ExamTypeId_CreatedAt",
                table: "ExamOrders",
                columns: new[] { "ExamTypeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamOrders_Status_CreatedAt",
                table: "ExamOrders",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExamOrders_CreatedAt",
                table: "ExamOrders");

            migrationBuilder.DropIndex(
                name: "IX_ExamOrders_ExamNumber",
                table: "ExamOrders");

            migrationBuilder.DropIndex(
                name: "IX_ExamOrders_ExamTypeId_CreatedAt",
                table: "ExamOrders");

            migrationBuilder.DropIndex(
                name: "IX_ExamOrders_Status_CreatedAt",
                table: "ExamOrders");

            migrationBuilder.DropColumn(
                name: "ExamNumber",
                table: "ExamOrders");

            migrationBuilder.CreateIndex(
                name: "IX_ExamOrders_ExamTypeId",
                table: "ExamOrders",
                column: "ExamTypeId");
        }
    }
}
