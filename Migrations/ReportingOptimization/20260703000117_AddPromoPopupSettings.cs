using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddPromoPopupSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnnouncementBgColor",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AnnouncementFontSize",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AnnouncementTextColor",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PromoPopupCoupon",
                table: "StoreSettings",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "PromoPopupEnabled",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PromoPopupImageUrl",
                table: "StoreSettings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PromoPopupText",
                table: "StoreSettings",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PromoPopupTitle",
                table: "StoreSettings",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnnouncementBgColor",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "AnnouncementFontSize",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "AnnouncementTextColor",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PromoPopupCoupon",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PromoPopupEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PromoPopupImageUrl",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PromoPopupText",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "PromoPopupTitle",
                table: "StoreSettings");
        }
    }
}
