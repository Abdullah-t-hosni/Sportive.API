using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddLinkedWarehouseIdToBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LinkedWarehouseId",
                table: "Branches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LinkedWarehouseId1",
                table: "Branches",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branches_LinkedWarehouseId1",
                table: "Branches",
                column: "LinkedWarehouseId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Branches_Warehouses_LinkedWarehouseId1",
                table: "Branches",
                column: "LinkedWarehouseId1",
                principalTable: "Warehouses",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Branches_Warehouses_LinkedWarehouseId1",
                table: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_Branches_LinkedWarehouseId1",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "LinkedWarehouseId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "LinkedWarehouseId1",
                table: "Branches");
        }
    }
}
