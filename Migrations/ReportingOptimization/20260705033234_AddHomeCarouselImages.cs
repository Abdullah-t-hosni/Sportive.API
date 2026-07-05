using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddHomeCarouselImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HomeCarouselImagesBottom",
                table: "StoreSettings",
                type: "varchar(3000)",
                maxLength: 3000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "HomeCarouselImagesTop",
                table: "StoreSettings",
                type: "varchar(3000)",
                maxLength: 3000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HomeCarouselImagesBottom",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "HomeCarouselImagesTop",
                table: "StoreSettings");
        }
    }
}
