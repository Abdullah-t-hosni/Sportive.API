using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Data.Migrations.StandaloneReturns
{
    /// <inheritdoc />
    public partial class AddUnitMultiplier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Multiplier",
                table: "ProductUnits",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Multiplier",
                table: "ProductUnits");
        }
    }
}
