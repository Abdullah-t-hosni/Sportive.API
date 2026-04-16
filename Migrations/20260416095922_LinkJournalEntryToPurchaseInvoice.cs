using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.DecimalFix
{
    /// <inheritdoc />
    public partial class LinkJournalEntryToPurchaseInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurchaseInvoiceId",
                table: "JournalLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseInvoiceId",
                table: "JournalEntries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_PurchaseInvoiceId",
                table: "JournalLines",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_PurchaseInvoiceId",
                table: "JournalEntries",
                column: "PurchaseInvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_PurchaseInvoices_PurchaseInvoiceId",
                table: "JournalEntries",
                column: "PurchaseInvoiceId",
                principalTable: "PurchaseInvoices",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalLines_PurchaseInvoices_PurchaseInvoiceId",
                table: "JournalLines",
                column: "PurchaseInvoiceId",
                principalTable: "PurchaseInvoices",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntries_PurchaseInvoices_PurchaseInvoiceId",
                table: "JournalEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalLines_PurchaseInvoices_PurchaseInvoiceId",
                table: "JournalLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalLines_PurchaseInvoiceId",
                table: "JournalLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_PurchaseInvoiceId",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "PurchaseInvoiceId",
                table: "JournalLines");

            migrationBuilder.DropColumn(
                name: "PurchaseInvoiceId",
                table: "JournalEntries");
        }
    }
}
