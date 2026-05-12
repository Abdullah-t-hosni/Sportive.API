using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddOvertimeToPayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalOvertimeAmount",
                table: "PayrollRuns",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OvertimeAmount",
                table: "PayrollItems",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OvertimeHours",
                table: "PayrollItems",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalOvertimeAmount",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "OvertimeAmount",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "OvertimeHours",
                table: "PayrollItems");
        }
    }
}
