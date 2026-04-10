using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations
{
    /// <inheritdoc />
    public partial class UpdateStoreSettingsV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TikTokPage",
                table: "StoreSettings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(200)",
                oldMaxLength: 200)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "InstagramPage",
                table: "StoreSettings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(200)",
                oldMaxLength: 200)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "FacebookPage",
                table: "StoreSettings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(200)",
                oldMaxLength: 200)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "AllowBackorders",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowGuestCheckout",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AllowedPaymentMethods",
                table: "StoreSettings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "AnnouncementEnabled",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AnnouncementText",
                table: "StoreSettings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "BrandColorH",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BrandColorL",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BrandColorS",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "StoreSettings",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "CurrencySymbol",
                table: "StoreSettings",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "EnableCoupons",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableReviews",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FaviconUrl",
                table: "StoreSettings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "HeroImageUrl",
                table: "StoreSettings",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "HeroSubtitle",
                table: "StoreSettings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "HeroTitle",
                table: "StoreSettings",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "HideOutOfStock",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "StoreSettings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "LowStockThreshold",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "MinOrderAmount",
                table: "StoreSettings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "OrderNumberPrefix",
                table: "StoreSettings",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ReceiptFooterText",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ReceiptHeaderText",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptShowBarcode",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptShowLogo",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReviewsRequirePurchase",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TwitterUrl",
                table: "StoreSettings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "YoutubeUrl",
                table: "StoreSettings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ShippingZones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    NameAr = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NameEn = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Governorates = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FreeThreshold = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EstimatedDays = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingZones", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                columns: new[] { "AllowBackorders", "AllowGuestCheckout", "AllowedPaymentMethods", "AnnouncementEnabled", "AnnouncementText", "BrandColorH", "BrandColorL", "BrandColorS", "CurrencyCode", "CurrencySymbol", "EnableCoupons", "EnableReviews", "FaviconUrl", "HeroImageUrl", "HeroSubtitle", "HeroTitle", "HideOutOfStock", "LogoUrl", "LowStockThreshold", "MinOrderAmount", "OrderNumberPrefix", "ReceiptFooterText", "ReceiptHeaderText", "ReceiptShowBarcode", "ReceiptShowLogo", "ReviewsRequirePurchase", "StoreSlogan", "TwitterUrl", "YoutubeUrl" },
                values: new object[] { false, true, "Cash,Vodafone,InstaPay", false, null, 221, 53, 83, "EGP", "ج.م", true, true, null, null, null, null, false, null, 5, 0m, "SPT", null, null, true, true, true, "Beyond Performance", null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShippingZones");

            migrationBuilder.DropColumn(
                name: "AllowBackorders",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "AllowGuestCheckout",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "AllowedPaymentMethods",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "AnnouncementEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "AnnouncementText",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BrandColorH",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BrandColorL",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "BrandColorS",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "CurrencySymbol",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "EnableCoupons",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "EnableReviews",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "FaviconUrl",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "HeroImageUrl",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "HeroSubtitle",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "HeroTitle",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "HideOutOfStock",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "LowStockThreshold",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "MinOrderAmount",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "OrderNumberPrefix",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptFooterText",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptHeaderText",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptShowBarcode",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReceiptShowLogo",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "ReviewsRequirePurchase",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "TwitterUrl",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "YoutubeUrl",
                table: "StoreSettings");

            migrationBuilder.AlterColumn<string>(
                name: "TikTokPage",
                table: "StoreSettings",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(500)",
                oldMaxLength: 500)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "InstagramPage",
                table: "StoreSettings",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(500)",
                oldMaxLength: 500)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "FacebookPage",
                table: "StoreSettings",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(500)",
                oldMaxLength: 500)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                column: "StoreSlogan",
                value: "Your Ultimate Sports Destination");
        }
    }
}
