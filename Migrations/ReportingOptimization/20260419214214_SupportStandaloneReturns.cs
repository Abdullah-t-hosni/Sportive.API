using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class SupportStandaloneReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReturns_PurchaseInvoices_PurchaseInvoiceId",
                table: "PurchaseReturns");

            migrationBuilder.AlterColumn<int>(
                name: "PurchaseInvoiceId",
                table: "PurchaseReturns",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "CashAccountId",
                table: "PurchaseReturns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentTerms",
                table: "PurchaseReturns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "PurchaseInvoiceItemId",
                table: "PurchaseReturnItems",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_CashAccountId",
                table: "PurchaseReturns",
                column: "CashAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReturns_Accounts_CashAccountId",
                table: "PurchaseReturns",
                column: "CashAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReturns_PurchaseInvoices_PurchaseInvoiceId",
                table: "PurchaseReturns",
                column: "PurchaseInvoiceId",
                principalTable: "PurchaseInvoices",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReturns_Accounts_CashAccountId",
                table: "PurchaseReturns");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReturns_PurchaseInvoices_PurchaseInvoiceId",
                table: "PurchaseReturns");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturns_CashAccountId",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "CashAccountId",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "PaymentTerms",
                table: "PurchaseReturns");

            migrationBuilder.AlterColumn<int>(
                name: "PurchaseInvoiceId",
                table: "PurchaseReturns",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PurchaseInvoiceItemId",
                table: "PurchaseReturnItems",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReturns_PurchaseInvoices_PurchaseInvoiceId",
                table: "PurchaseReturns",
                column: "PurchaseInvoiceId",
                principalTable: "PurchaseInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
