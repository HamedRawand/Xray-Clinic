using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NasimXray.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExamDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "ExamOrders",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DiscountReason",
                table: "ExamOrders",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DiscountedAt",
                table: "ExamOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiscountedById",
                table: "ExamOrders",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamOrders_DiscountedById",
                table: "ExamOrders",
                column: "DiscountedById");

            migrationBuilder.AddForeignKey(
                name: "FK_ExamOrders_Users_DiscountedById",
                table: "ExamOrders",
                column: "DiscountedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExamOrders_Users_DiscountedById",
                table: "ExamOrders");

            migrationBuilder.DropIndex(
                name: "IX_ExamOrders_DiscountedById",
                table: "ExamOrders");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "ExamOrders");

            migrationBuilder.DropColumn(
                name: "DiscountReason",
                table: "ExamOrders");

            migrationBuilder.DropColumn(
                name: "DiscountedAt",
                table: "ExamOrders");

            migrationBuilder.DropColumn(
                name: "DiscountedById",
                table: "ExamOrders");
        }
    }
}
