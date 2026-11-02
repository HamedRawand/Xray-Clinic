using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NasimXray.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExamWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "ExamOrders",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PerformedAt",
                table: "ExamOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PerformedById",
                table: "ExamOrders",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamOrders_PerformedById",
                table: "ExamOrders",
                column: "PerformedById");

            migrationBuilder.AddForeignKey(
                name: "FK_ExamOrders_Users_PerformedById",
                table: "ExamOrders",
                column: "PerformedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExamOrders_Users_PerformedById",
                table: "ExamOrders");

            migrationBuilder.DropIndex(
                name: "IX_ExamOrders_PerformedById",
                table: "ExamOrders");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "ExamOrders");

            migrationBuilder.DropColumn(
                name: "PerformedAt",
                table: "ExamOrders");

            migrationBuilder.DropColumn(
                name: "PerformedById",
                table: "ExamOrders");
        }
    }
}
