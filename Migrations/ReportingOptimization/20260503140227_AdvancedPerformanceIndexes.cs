using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AdvancedPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // All changes in this migration were already applied manually via SchemaFixController.
            // Keeping this empty so EF Core marks it as applied without re-running anything.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_IsArchived",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_SalesPersonId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_JournalLines_CostCenter",
                table: "JournalLines");

            migrationBuilder.DropColumn(
                name: "ReceiptShowSKU",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptShowTime",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SKU",
                table: "OrderItems");

            migrationBuilder.AlterColumn<string>(
                name: "SalesPersonId",
                table: "Orders",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
