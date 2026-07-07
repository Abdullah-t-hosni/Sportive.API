using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddHomeCategoryImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HomeCategoryEquipmentImage",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "HomeCategoryKidsImage",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "HomeCategoryMenImage",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "HomeCategorySpecialSizesImage",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "HomeCategoryWomenImage",
                table: "StoreSettings",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HomeCategoryEquipmentImage",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "HomeCategoryKidsImage",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "HomeCategoryMenImage",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "HomeCategorySpecialSizesImage",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "HomeCategoryWomenImage",
                table: "StoreSettings");
        }
    }
}
