using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class SplitWapilotInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "WapilotInstanceId",
                table: "StoreSettings",
                newName: "WapilotWebInstanceId");

            migrationBuilder.AddColumn<string>(
                name: "WapilotPosInstanceId",
                table: "StoreSettings",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WapilotPosInstanceId",
                table: "StoreSettings");

            migrationBuilder.RenameColumn(
                name: "WapilotWebInstanceId",
                table: "StoreSettings",
                newName: "WapilotInstanceId");
        }
    }
}
