using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddReceiptLogoSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderStatusAfterPrint",
                table: "StoreSettings",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "QzA4Printer",
                table: "StoreSettings",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "QzBarcodePrinter",
                table: "StoreSettings",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "QzReceiptPrinter",
                table: "StoreSettings",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ReceiptFontFamily",
                table: "StoreSettings",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "ReceiptFontSize",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptLogoPosition",
                table: "StoreSettings",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "ReceiptLogoWidth",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptPaperSize",
                table: "StoreSettings",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "ReceiptWidth",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                columns: new[] { "OrderStatusAfterPrint", "QzA4Printer", "QzBarcodePrinter", "QzReceiptPrinter", "ReceiptFontFamily", "ReceiptFontSize", "ReceiptLogoPosition", "ReceiptLogoWidth", "ReceiptPaperSize", "ReceiptWidth" },
                values: new object[] { null, null, null, null, "Alexandria", 11, "center", 80, "Receipt", 80 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrderStatusAfterPrint",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "QzA4Printer",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "QzBarcodePrinter",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "QzReceiptPrinter",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptFontFamily",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptFontSize",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptLogoPosition",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptLogoWidth",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptPaperSize",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptWidth",
                table: "StoreSettings");
        }
    }
}
