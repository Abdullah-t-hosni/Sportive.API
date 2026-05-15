using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddMoreReceiptSettingsV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "ReceiptDensity",
                table: "StoreSettings",
                type: "double",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "CommercialRegister",
                table: "StoreSettings",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptShowCashier",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptShowDiscount",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptShowNote",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptShowTax",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptShowUnitPrice",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TaxNumber",
                table: "StoreSettings",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                columns: new[] { "CommercialRegister", "ReceiptDensity", "ReceiptSectionsOrder", "ReceiptShowCashier", "ReceiptShowDiscount", "ReceiptShowNote", "ReceiptShowTax", "ReceiptShowUnitPrice", "ReceiptSoftwareProvider", "TaxNumber" },
                values: new object[] { null, 1.3999999999999999, "header,order_info,items_table,totals_area,tafqeet,payment_info,customer_signature,footer_text,terms_conditions,barcode", true, true, true, true, true, "Eng.Abdullah-Taha", null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommercialRegister",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptShowCashier",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptShowDiscount",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptShowNote",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptShowTax",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptShowUnitPrice",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "TaxNumber",
                table: "StoreSettings");

            migrationBuilder.AlterColumn<int>(
                name: "ReceiptDensity",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "double");

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                columns: new[] { "ReceiptDensity", "ReceiptSectionsOrder", "ReceiptSoftwareProvider" },
                values: new object[] { 2, "header,order_info,items_table,totals_area,tafqeet,payment_info,footer_text,terms_conditions,barcode", "By Easy Store" });
        }
    }
}
