using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddBranchIdToFixedAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "FixedAssets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "AssetDisposals",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "AssetDepreciations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_BranchId",
                table: "FixedAssets",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDisposals_BranchId",
                table: "AssetDisposals",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetDepreciations_BranchId",
                table: "AssetDepreciations",
                column: "BranchId");

            migrationBuilder.AddForeignKey(
                name: "FK_AssetDepreciations_Branches_BranchId",
                table: "AssetDepreciations",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AssetDisposals_Branches_BranchId",
                table: "AssetDisposals",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssets_Branches_BranchId",
                table: "FixedAssets",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssetDepreciations_Branches_BranchId",
                table: "AssetDepreciations");

            migrationBuilder.DropForeignKey(
                name: "FK_AssetDisposals_Branches_BranchId",
                table: "AssetDisposals");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssets_Branches_BranchId",
                table: "FixedAssets");

            migrationBuilder.DropIndex(
                name: "IX_FixedAssets_BranchId",
                table: "FixedAssets");

            migrationBuilder.DropIndex(
                name: "IX_AssetDisposals_BranchId",
                table: "AssetDisposals");

            migrationBuilder.DropIndex(
                name: "IX_AssetDepreciations_BranchId",
                table: "AssetDepreciations");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "AssetDisposals");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "AssetDepreciations");
        }
    }
}
