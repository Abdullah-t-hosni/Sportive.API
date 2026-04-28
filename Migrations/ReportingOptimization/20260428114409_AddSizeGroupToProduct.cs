using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddSizeGroupToProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SizeGroupId",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_SizeGroupId",
                table: "Products",
                column: "SizeGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_SizeGroups_SizeGroupId",
                table: "Products",
                column: "SizeGroupId",
                principalTable: "SizeGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_SizeGroups_SizeGroupId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_SizeGroupId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SizeGroupId",
                table: "Products");
        }
    }
}
