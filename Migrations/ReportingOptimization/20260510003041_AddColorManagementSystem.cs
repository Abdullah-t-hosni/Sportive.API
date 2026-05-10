using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddColorManagementSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ColorGroupId",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ColorGroupId",
                table: "Categories",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ColorGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColorGroups", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ColorValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ColorGroupId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColorValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ColorValues_ColorGroups_ColorGroupId",
                        column: x => x.ColorGroupId,
                        principalTable: "ColorGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Products_ColorGroupId",
                table: "Products",
                column: "ColorGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ColorGroupId",
                table: "Categories",
                column: "ColorGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ColorValues_ColorGroupId",
                table: "ColorValues",
                column: "ColorGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_ColorGroups_ColorGroupId",
                table: "Categories",
                column: "ColorGroupId",
                principalTable: "ColorGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_ColorGroups_ColorGroupId",
                table: "Products",
                column: "ColorGroupId",
                principalTable: "ColorGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Categories_ColorGroups_ColorGroupId",
                table: "Categories");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_ColorGroups_ColorGroupId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "ColorValues");

            migrationBuilder.DropTable(
                name: "ColorGroups");

            migrationBuilder.DropIndex(
                name: "IX_Products_ColorGroupId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Categories_ColorGroupId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "ColorGroupId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ColorGroupId",
                table: "Categories");
        }
    }
}
