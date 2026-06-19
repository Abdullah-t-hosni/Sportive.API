using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase3EtaIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EgsCode",
                table: "Products",
                newName: "SaudiProductCode");

            migrationBuilder.AddColumn<string>(
                name: "EtaPosSerial",
                table: "StoreSettings",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "EtaSignatureType",
                table: "StoreSettings",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "EtaTaxNumber",
                table: "StoreSettings",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ZatcaTaxNumber",
                table: "StoreSettings",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "EgyptianProductCode",
                table: "Products",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EtaPosSerial",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "EtaSignatureType",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "EtaTaxNumber",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ZatcaTaxNumber",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "EgyptianProductCode",
                table: "Products");

            migrationBuilder.RenameColumn(
                name: "SaudiProductCode",
                table: "Products",
                newName: "EgsCode");
        }
    }
}
