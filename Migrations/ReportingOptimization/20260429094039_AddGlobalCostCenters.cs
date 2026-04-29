using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddGlobalCostCenters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CostCenter",
                table: "InventoryOpeningBalances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CostCenter",
                table: "Employees",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CostCenter",
                table: "EmployeeDeductions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CostCenter",
                table: "EmployeeBonuses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CostCenter",
                table: "EmployeeAdvances",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostCenter",
                table: "InventoryOpeningBalances");

            migrationBuilder.DropColumn(
                name: "CostCenter",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "CostCenter",
                table: "EmployeeDeductions");

            migrationBuilder.DropColumn(
                name: "CostCenter",
                table: "EmployeeBonuses");

            migrationBuilder.DropColumn(
                name: "CostCenter",
                table: "EmployeeAdvances");
        }
    }
}
