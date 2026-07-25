using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiCategoryAndImageCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "ProductImages",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductSecondaryCategories",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSecondaryCategories", x => new { x.ProductId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_ProductSecondaryCategories_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductSecondaryCategories_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_CategoryId",
                table: "ProductImages",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSecondaryCategories_CategoryId",
                table: "ProductSecondaryCategories",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductImages_Categories_CategoryId",
                table: "ProductImages",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductImages_Categories_CategoryId",
                table: "ProductImages");

            migrationBuilder.DropTable(
                name: "ProductSecondaryCategories");

            migrationBuilder.DropIndex(
                name: "IX_ProductImages_CategoryId",
                table: "ProductImages");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "ProductImages");
        }
    }
}
