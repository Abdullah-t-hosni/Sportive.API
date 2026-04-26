using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddStoreSettingsFinal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoPrintReceipt",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BarcodeShowColor",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BarcodeShowName",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BarcodeShowPrice",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BarcodeShowSize",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FacebookPixelId",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "GoogleAnalyticsId",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "OrderSuccessMessageAr",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "OrderSuccessMessageEn",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "ReceiptExtraCopies",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SiteKeywords",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SiteMetaDescriptionAr",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SiteMetaDescriptionEn",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppOrderTemplate",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppReturnTemplate",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppShippingTemplate",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                columns: new[] { "AutoPrintReceipt", "BarcodeShowColor", "BarcodeShowName", "BarcodeShowPrice", "BarcodeShowSize", "FacebookPixelId", "GoogleAnalyticsId", "OrderSuccessMessageAr", "OrderSuccessMessageEn", "ReceiptExtraCopies", "SiteKeywords", "SiteMetaDescriptionAr", "SiteMetaDescriptionEn", "WhatsAppOrderTemplate", "WhatsAppReturnTemplate", "WhatsAppShippingTemplate" },
                values: new object[] { false, true, true, true, true, null, null, "شكراً لتسوقك معنا! سيقوم فريقنا بالتواصل معك قريباً لتأكيد الطلب.", "Thank you for shopping with us! Our team will contact you soon to confirm your order.", 0, null, null, null, "أهلاً {customerName}، تم استلام طلبك رقم #{orderNumber} وجاري التجهيز.", "تم استلام طلب المرتجع الخاص بك رقم #{orderNumber}، وجاري مراجعته.", "طلبك #{orderNumber} في الطريق مع المندوب، سيصلك قريباً." });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoPrintReceipt",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeShowColor",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeShowName",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeShowPrice",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BarcodeShowSize",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "FacebookPixelId",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "GoogleAnalyticsId",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "OrderSuccessMessageAr",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "OrderSuccessMessageEn",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptExtraCopies",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SiteKeywords",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SiteMetaDescriptionAr",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "SiteMetaDescriptionEn",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppOrderTemplate",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppReturnTemplate",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppShippingTemplate",
                table: "StoreSettings");
        }
    }
}
