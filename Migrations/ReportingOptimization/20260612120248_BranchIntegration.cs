using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class BranchIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "PurchaseReturns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "PurchaseInvoices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "PayrollRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "InventoryMovements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "EmployeeDeductions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "EmployeeBonuses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "EmployeeAdvances",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_BranchId",
                table: "PurchaseReturns",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_BranchId",
                table: "PurchaseInvoices",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_BranchId",
                table: "PayrollRuns",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_BranchId",
                table: "InventoryMovements",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDeductions_BranchId",
                table: "EmployeeDeductions",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeBonuses_BranchId",
                table: "EmployeeBonuses",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAdvances_BranchId",
                table: "EmployeeAdvances",
                column: "BranchId");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeAdvances_Branches_BranchId",
                table: "EmployeeAdvances",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeBonuses_Branches_BranchId",
                table: "EmployeeBonuses",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeDeductions_Branches_BranchId",
                table: "EmployeeDeductions",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Branches_BranchId",
                table: "InventoryMovements",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollRuns_Branches_BranchId",
                table: "PayrollRuns",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoices_Branches_BranchId",
                table: "PurchaseInvoices",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReturns_Branches_BranchId",
                table: "PurchaseReturns",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeAdvances_Branches_BranchId",
                table: "EmployeeAdvances");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeBonuses_Branches_BranchId",
                table: "EmployeeBonuses");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeDeductions_Branches_BranchId",
                table: "EmployeeDeductions");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_Branches_BranchId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollRuns_Branches_BranchId",
                table: "PayrollRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoices_Branches_BranchId",
                table: "PurchaseInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReturns_Branches_BranchId",
                table: "PurchaseReturns");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturns_BranchId",
                table: "PurchaseReturns");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoices_BranchId",
                table: "PurchaseInvoices");

            migrationBuilder.DropIndex(
                name: "IX_PayrollRuns_BranchId",
                table: "PayrollRuns");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_BranchId",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeDeductions_BranchId",
                table: "EmployeeDeductions");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeBonuses_BranchId",
                table: "EmployeeBonuses");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeAdvances_BranchId",
                table: "EmployeeAdvances");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "EmployeeDeductions");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "EmployeeBonuses");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "EmployeeAdvances");
        }
    }
}
