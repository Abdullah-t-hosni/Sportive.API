using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations
{
    /// <inheritdoc />
    public partial class FixModelDiscrepancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /* 
            migrationBuilder.AddColumn<string>(
                name: "BackupTime",
                table: "StoreSettings",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "BackupUtcOffset",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                columns: new[] { "BackupTime", "BackupUtcOffset" },
                values: new object[] { "02:00", 2 });
            */

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_CustomerId",
                table: "JournalLines",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_SupplierId",
                table: "JournalLines",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalLines_Customers_CustomerId",
                table: "JournalLines",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalLines_Suppliers_SupplierId",
                table: "JournalLines",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JournalLines_Customers_CustomerId",
                table: "JournalLines");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalLines_Suppliers_SupplierId",
                table: "JournalLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalLines_CustomerId",
                table: "JournalLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalLines_SupplierId",
                table: "JournalLines");

            migrationBuilder.DropColumn(
                name: "BackupTime",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BackupUtcOffset",
                table: "StoreSettings");
        }
    }
}
