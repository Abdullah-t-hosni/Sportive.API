using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddBarcodeAdvancedSettingsFinal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BarcodeCodeFontSize",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BarcodeMarginLeft",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BarcodeMarginTop",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BarcodeNameFontSize",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BarcodePriceFontSize",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BarcodeStoreFontSize",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BarcodeVariantFontSize",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                columns: new[] { "BarcodeCodeFontSize", "BarcodeLabelHeight", "BarcodeLabelWidth", "BarcodeMarginLeft", "BarcodeMarginTop", "BarcodeNameFontSize", "BarcodePriceFontSize", "BarcodeStoreFontSize", "BarcodeSvgHeight", "BarcodeSvgWidth", "BarcodeVariantFontSize" },
                values: new object[] { 15, 30, 50, 0, 0, 10, 14, 8, 10, 40, 9 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BarcodeCodeFontSize",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeMarginLeft",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeMarginTop",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeNameFontSize",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodePriceFontSize",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeStoreFontSize",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeVariantFontSize",
                table: "StoreSettings");

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                columns: new[] { "BarcodeLabelHeight", "BarcodeLabelWidth", "BarcodeSvgHeight", "BarcodeSvgWidth" },
                values: new object[] { 25, 40, 50, 180 });
        }
    }
}
