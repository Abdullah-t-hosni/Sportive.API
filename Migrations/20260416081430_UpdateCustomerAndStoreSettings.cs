using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.DecimalFix
{
    /// <inheritdoc />
    public partial class UpdateCustomerAndStoreSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalPaid",
                table: "Customers",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalSales",
                table: "Customers",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                column: "StoreEmailAddr",
                value: "contact@sportive-sportwear.com");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalPaid",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TotalSales",
                table: "Customers");

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                column: "StoreEmailAddr",
                value: "");
        }
    }
}
