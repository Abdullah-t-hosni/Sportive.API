using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class HR_Refinements_And_Fixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CostCenter",
                table: "FixedAssets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CostCenter",
                table: "FixedAssetCategories",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostCenter",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "CostCenter",
                table: "FixedAssetCategories");
        }
    }
}
