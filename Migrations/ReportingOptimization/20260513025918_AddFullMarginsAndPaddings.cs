using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddFullMarginsAndPaddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BarcodeMarginBottom",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BarcodeMarginRight",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BarcodePaddingBottom",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BarcodePaddingLeft",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BarcodePaddingRight",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BarcodePaddingTop",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                columns: new[] { "BarcodeMarginBottom", "BarcodeMarginRight", "BarcodePaddingBottom", "BarcodePaddingLeft", "BarcodePaddingRight", "BarcodePaddingTop" },
                values: new object[] { 0, 0, 0, 0, 0, 0 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BarcodeMarginBottom",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeMarginRight",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodePaddingBottom",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodePaddingLeft",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodePaddingRight",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodePaddingTop",
                table: "StoreSettings");
        }
    }
}
