using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthlyVacationDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableUrgencyTags",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LinkedProductId",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MonthlyVacationDays",
                table: "Employees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Products_LinkedProductId",
                table: "Products",
                column: "LinkedProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Products_LinkedProductId",
                table: "Products",
                column: "LinkedProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Products_LinkedProductId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_LinkedProductId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "EnableUrgencyTags",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "LinkedProductId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "MonthlyVacationDays",
                table: "Employees");
        }
    }
}
