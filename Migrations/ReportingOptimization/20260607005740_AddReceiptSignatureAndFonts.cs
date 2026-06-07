using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddReceiptSignatureAndFonts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReceiptFooterFontSize",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReceiptHeaderFontSize",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReceiptItemsFontSize",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptShowRecipientSignature",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptShowStoreSeal",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReceiptStoreNameFontSize",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReceiptTotalsFontSize",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceiptFooterFontSize",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptHeaderFontSize",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptItemsFontSize",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptShowRecipientSignature",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptShowStoreSeal",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptStoreNameFontSize",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptTotalsFontSize",
                table: "StoreSettings");
        }
    }
}
