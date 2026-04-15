using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.DecimalFix
{
    /// <inheritdoc />
    public partial class AddReceiptCustomizationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReceiptComplaintsPhone",
                table: "StoreSettings",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptShowAddress",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptShowBalance",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptShowCustomerDetails",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptShowItemCount",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptShowPhone",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptShowTotalPieceCount",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptSoftwareProvider",
                table: "StoreSettings",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ReceiptTermsAndConditions",
                table: "StoreSettings",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                columns: new[] { "ReceiptComplaintsPhone", "ReceiptShowAddress", "ReceiptShowBalance", "ReceiptShowCustomerDetails", "ReceiptShowItemCount", "ReceiptShowPhone", "ReceiptShowTotalPieceCount", "ReceiptSoftwareProvider", "ReceiptTermsAndConditions" },
                values: new object[] { null, true, true, true, true, true, true, "By Easy Store", null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceiptComplaintsPhone",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptShowAddress",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptShowBalance",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptShowCustomerDetails",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptShowItemCount",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptShowPhone",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptShowTotalPieceCount",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptSoftwareProvider",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptTermsAndConditions",
                table: "StoreSettings");
        }
    }
}
