using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddReportIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Department",
                table: "Employees");

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalance",
                table: "Suppliers",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "CostCenter",
                table: "ReceiptVouchers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CostCenter",
                table: "PaymentVouchers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CostCenter",
                table: "JournalLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CostCenter",
                table: "JournalEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DepartmentId",
                table: "Employees",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FixedAllowance",
                table: "Employees",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FixedDeduction",
                table: "Employees",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "CashAccountId",
                table: "EmployeeDeductions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "JournalEntryId",
                table: "EmployeeDeductions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CashAccountId",
                table: "EmployeeBonuses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "JournalEntryId",
                table: "EmployeeBonuses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_InvoiceDate",
                table: "PurchaseInvoices",
                column: "InvoiceDate");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_EntryDate",
                table: "JournalEntries",
                column: "EntryDate");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_DepartmentId",
                table: "Employees",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDeductions_CashAccountId",
                table: "EmployeeDeductions",
                column: "CashAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeDeductions_JournalEntryId",
                table: "EmployeeDeductions",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeBonuses_CashAccountId",
                table: "EmployeeBonuses",
                column: "CashAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeBonuses_JournalEntryId",
                table: "EmployeeBonuses",
                column: "JournalEntryId");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeBonuses_Accounts_CashAccountId",
                table: "EmployeeBonuses",
                column: "CashAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeBonuses_JournalEntries_JournalEntryId",
                table: "EmployeeBonuses",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeDeductions_Accounts_CashAccountId",
                table: "EmployeeDeductions",
                column: "CashAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeDeductions_JournalEntries_JournalEntryId",
                table: "EmployeeDeductions",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Departments_DepartmentId",
                table: "Employees",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeBonuses_Accounts_CashAccountId",
                table: "EmployeeBonuses");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeBonuses_JournalEntries_JournalEntryId",
                table: "EmployeeBonuses");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeDeductions_Accounts_CashAccountId",
                table: "EmployeeDeductions");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeDeductions_JournalEntries_JournalEntryId",
                table: "EmployeeDeductions");

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Departments_DepartmentId",
                table: "Employees");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoices_InvoiceDate",
                table: "PurchaseInvoices");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_EntryDate",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_Employees_DepartmentId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeDeductions_CashAccountId",
                table: "EmployeeDeductions");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeDeductions_JournalEntryId",
                table: "EmployeeDeductions");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeBonuses_CashAccountId",
                table: "EmployeeBonuses");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeBonuses_JournalEntryId",
                table: "EmployeeBonuses");

            migrationBuilder.DropColumn(
                name: "OpeningBalance",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "CostCenter",
                table: "ReceiptVouchers");

            migrationBuilder.DropColumn(
                name: "CostCenter",
                table: "PaymentVouchers");

            migrationBuilder.DropColumn(
                name: "CostCenter",
                table: "JournalLines");

            migrationBuilder.DropColumn(
                name: "CostCenter",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "FixedAllowance",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "FixedDeduction",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "CashAccountId",
                table: "EmployeeDeductions");

            migrationBuilder.DropColumn(
                name: "JournalEntryId",
                table: "EmployeeDeductions");

            migrationBuilder.DropColumn(
                name: "CashAccountId",
                table: "EmployeeBonuses");

            migrationBuilder.DropColumn(
                name: "JournalEntryId",
                table: "EmployeeBonuses");

            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "Employees",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
