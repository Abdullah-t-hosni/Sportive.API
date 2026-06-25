using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskBlueprints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TaskBlueprintId",
                table: "EmployeeTasks",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaskBlueprints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    ResponsibilityTypeId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ActiveDaysOfWeek = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TaskBehavior = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetQuantity = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    RewardAmount = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    PenaltyAmount = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    CriteriaJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskBlueprints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskBlueprints_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskBlueprints_ResponsibilityTypes_ResponsibilityTypeId",
                        column: x => x.ResponsibilityTypeId,
                        principalTable: "ResponsibilityTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTasks_TaskBlueprintId",
                table: "EmployeeTasks",
                column: "TaskBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskBlueprints_EmployeeId",
                table: "TaskBlueprints",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskBlueprints_ResponsibilityTypeId",
                table: "TaskBlueprints",
                column: "ResponsibilityTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeTasks_TaskBlueprints_TaskBlueprintId",
                table: "EmployeeTasks",
                column: "TaskBlueprintId",
                principalTable: "TaskBlueprints",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeTasks_TaskBlueprints_TaskBlueprintId",
                table: "EmployeeTasks");

            migrationBuilder.DropTable(
                name: "TaskBlueprints");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeTasks_TaskBlueprintId",
                table: "EmployeeTasks");

            migrationBuilder.DropColumn(
                name: "TaskBlueprintId",
                table: "EmployeeTasks");
        }
    }
}
