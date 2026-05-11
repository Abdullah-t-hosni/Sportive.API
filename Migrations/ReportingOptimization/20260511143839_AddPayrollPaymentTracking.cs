using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddPayrollPaymentTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaymentJournalEntryId",
                table: "PayrollRuns",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_PaymentJournalEntryId",
                table: "PayrollRuns",
                column: "PaymentJournalEntryId");

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollRuns_JournalEntries_PaymentJournalEntryId",
                table: "PayrollRuns",
                column: "PaymentJournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayrollRuns_JournalEntries_PaymentJournalEntryId",
                table: "PayrollRuns");

            migrationBuilder.DropIndex(
                name: "IX_PayrollRuns_PaymentJournalEntryId",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "PaymentJournalEntryId",
                table: "PayrollRuns");
        }
    }
}
