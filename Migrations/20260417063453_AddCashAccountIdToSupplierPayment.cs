using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.DecimalFix
{
    /// <inheritdoc />
    public partial class AddCashAccountIdToSupplierPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CashAccountId",
                table: "SupplierPayments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_CashAccountId",
                table: "SupplierPayments",
                column: "CashAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierPayments_Accounts_CashAccountId",
                table: "SupplierPayments",
                column: "CashAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupplierPayments_Accounts_CashAccountId",
                table: "SupplierPayments");

            migrationBuilder.DropIndex(
                name: "IX_SupplierPayments_CashAccountId",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "CashAccountId",
                table: "SupplierPayments");
        }
    }
}
