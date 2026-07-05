using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddHomeFeaturedCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HomeFeaturedCategories",
                table: "StoreSettings",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HomeFeaturedCategories",
                table: "StoreSettings");
        }
    }
}
