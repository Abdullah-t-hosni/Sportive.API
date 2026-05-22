using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class LinkInstallmentToReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReceiptVoucherId",
                table: "InstallmentPayments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstallmentPayments_ReceiptVoucherId",
                table: "InstallmentPayments",
                column: "ReceiptVoucherId");

            migrationBuilder.AddForeignKey(
                name: "FK_InstallmentPayments_ReceiptVouchers_ReceiptVoucherId",
                table: "InstallmentPayments",
                column: "ReceiptVoucherId",
                principalTable: "ReceiptVouchers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InstallmentPayments_ReceiptVouchers_ReceiptVoucherId",
                table: "InstallmentPayments");

            migrationBuilder.DropIndex(
                name: "IX_InstallmentPayments_ReceiptVoucherId",
                table: "InstallmentPayments");

            migrationBuilder.DropColumn(
                name: "ReceiptVoucherId",
                table: "InstallmentPayments");
        }
    }
}
