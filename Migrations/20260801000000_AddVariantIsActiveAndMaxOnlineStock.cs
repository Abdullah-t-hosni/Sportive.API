using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantIsActiveAndMaxOnlineStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ProductVariants",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxOnlineStock",
                table: "ProductVariants",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "MaxOnlineStock",
                table: "ProductVariants");
        }
    }
}
