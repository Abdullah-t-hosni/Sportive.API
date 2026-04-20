using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddEmployeeToVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmployeeId",
                table: "ReceiptVouchers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CostCenter",
                table: "PurchaseReturns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CostCenter",
                table: "PurchaseInvoices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmployeeId",
                table: "PaymentVouchers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVouchers_EmployeeId",
                table: "ReceiptVouchers",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentVouchers_EmployeeId",
                table: "PaymentVouchers",
                column: "EmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentVouchers_Employees_EmployeeId",
                table: "PaymentVouchers",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ReceiptVouchers_Employees_EmployeeId",
                table: "ReceiptVouchers",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentVouchers_Employees_EmployeeId",
                table: "PaymentVouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_ReceiptVouchers_Employees_EmployeeId",
                table: "ReceiptVouchers");

            migrationBuilder.DropIndex(
                name: "IX_ReceiptVouchers_EmployeeId",
                table: "ReceiptVouchers");

            migrationBuilder.DropIndex(
                name: "IX_PaymentVouchers_EmployeeId",
                table: "PaymentVouchers");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                table: "ReceiptVouchers");

            migrationBuilder.DropColumn(
                name: "CostCenter",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "CostCenter",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                table: "PaymentVouchers");
        }
    }
}
