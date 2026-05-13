using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddBarcodeDirection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BarcodeDirection",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                column: "BarcodeDirection",
                value: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BarcodeDirection",
                table: "StoreSettings");
        }
    }
}
