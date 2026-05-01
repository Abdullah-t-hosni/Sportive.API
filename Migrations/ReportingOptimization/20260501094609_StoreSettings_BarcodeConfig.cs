using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class StoreSettings_BarcodeConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BarcodeLabelHeight",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BarcodeLabelWidth",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "BarcodeShowStoreName",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BarcodeSvgHeight",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BarcodeSvgWidth",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                columns: new[] { "BarcodeLabelHeight", "BarcodeLabelWidth", "BarcodeShowStoreName", "BarcodeSvgHeight", "BarcodeSvgWidth" },
                values: new object[] { 25, 40, true, 50, 180 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BarcodeLabelHeight",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeLabelWidth",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeShowStoreName",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeSvgHeight",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeSvgWidth",
                table: "StoreSettings");
        }
    }
}
