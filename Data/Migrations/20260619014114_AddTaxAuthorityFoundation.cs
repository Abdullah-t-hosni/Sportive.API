using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxAuthorityFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EtaClientId",
                table: "StoreSettings",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "EtaClientSecret",
                table: "StoreSettings",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "EtaEnvironment",
                table: "StoreSettings",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "TaxAuthorityType",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ZatcaCertificate",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ZatcaEnvironment",
                table: "StoreSettings",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "EgsCode",
                table: "Products",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsSubmittedToTaxAuthority",
                table: "Orders",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TaxAuthorityQrCode",
                table: "Orders",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "TaxAuthorityReference",
                table: "Orders",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "TaxAuthorityStatus",
                table: "Orders",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EtaClientId",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "EtaClientSecret",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "EtaEnvironment",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "TaxAuthorityType",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ZatcaCertificate",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ZatcaEnvironment",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "EgsCode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "IsSubmittedToTaxAuthority",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TaxAuthorityQrCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TaxAuthorityReference",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TaxAuthorityStatus",
                table: "Orders");
        }
    }
}
