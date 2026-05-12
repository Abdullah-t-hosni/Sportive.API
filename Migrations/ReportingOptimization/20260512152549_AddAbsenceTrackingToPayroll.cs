using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddAbsenceTrackingToPayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalAbsenceDeduction",
                table: "PayrollRuns",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "AbsenceDays",
                table: "PayrollItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "AbsenceDeduction",
                table: "PayrollItems",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalAbsenceDeduction",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "AbsenceDays",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "AbsenceDeduction",
                table: "PayrollItems");
        }
    }
}
