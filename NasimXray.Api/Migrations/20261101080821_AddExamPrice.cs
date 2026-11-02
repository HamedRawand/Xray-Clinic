using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NasimXray.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExamPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "ExamOrders",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            // Existing exams: the best record of what they cost is the exam type's price today...
            migrationBuilder.Sql(
                "UPDATE \"ExamOrders\" o SET \"Price\" = t.\"Price\" FROM \"ExamTypes\" t WHERE t.\"Id\" = o.\"ExamTypeId\";");

            // ...except an exam that is alone on an invoice, where the invoice total is exactly what was charged.
            migrationBuilder.Sql(
                "UPDATE \"ExamOrders\" o SET \"Price\" = i.\"Total\" FROM \"Invoices\" i " +
                "WHERE o.\"InvoiceId\" = i.\"Id\" " +
                "AND (SELECT COUNT(*) FROM \"ExamOrders\" x WHERE x.\"InvoiceId\" = i.\"Id\") = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Price",
                table: "ExamOrders");
        }
    }
}
