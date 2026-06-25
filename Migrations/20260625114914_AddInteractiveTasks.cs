using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations
{
    /// <inheritdoc />
    public partial class AddInteractiveTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TaskConfigJson",
                table: "TaskBlueprints",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "TaskType",
                table: "TaskBlueprints",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReferenceId",
                table: "EmployeeTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaskConfigJson",
                table: "EmployeeTasks",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "TaskType",
                table: "EmployeeTasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "MediaUrl",
                table: "EmployeeTaskItems",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "ProductVariantId",
                table: "EmployeeTaskItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReferenceId",
                table: "EmployeeTaskItems",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaskConfigJson",
                table: "TaskBlueprints");

            migrationBuilder.DropColumn(
                name: "TaskType",
                table: "TaskBlueprints");

            migrationBuilder.DropColumn(
                name: "ReferenceId",
                table: "EmployeeTasks");

            migrationBuilder.DropColumn(
                name: "TaskConfigJson",
                table: "EmployeeTasks");

            migrationBuilder.DropColumn(
                name: "TaskType",
                table: "EmployeeTasks");

            migrationBuilder.DropColumn(
                name: "MediaUrl",
                table: "EmployeeTaskItems");

            migrationBuilder.DropColumn(
                name: "ProductVariantId",
                table: "EmployeeTaskItems");

            migrationBuilder.DropColumn(
                name: "ReferenceId",
                table: "EmployeeTaskItems");
        }
    }
}
