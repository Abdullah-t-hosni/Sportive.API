using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddLocationsToInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "InventoryOpeningBalances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "InventoryOpeningBalances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "InventoryAudits",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOpeningBalances_BranchId",
                table: "InventoryOpeningBalances",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOpeningBalances_WarehouseId",
                table: "InventoryOpeningBalances",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAudits_BranchId",
                table: "InventoryAudits",
                column: "BranchId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryAudits_Branches_BranchId",
                table: "InventoryAudits",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryOpeningBalances_Branches_BranchId",
                table: "InventoryOpeningBalances",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryOpeningBalances_Warehouses_WarehouseId",
                table: "InventoryOpeningBalances",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryAudits_Branches_BranchId",
                table: "InventoryAudits");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryOpeningBalances_Branches_BranchId",
                table: "InventoryOpeningBalances");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryOpeningBalances_Warehouses_WarehouseId",
                table: "InventoryOpeningBalances");

            migrationBuilder.DropIndex(
                name: "IX_InventoryOpeningBalances_BranchId",
                table: "InventoryOpeningBalances");

            migrationBuilder.DropIndex(
                name: "IX_InventoryOpeningBalances_WarehouseId",
                table: "InventoryOpeningBalances");

            migrationBuilder.DropIndex(
                name: "IX_InventoryAudits_BranchId",
                table: "InventoryAudits");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "InventoryOpeningBalances");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "InventoryOpeningBalances");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "InventoryAudits");
        }
    }
}
