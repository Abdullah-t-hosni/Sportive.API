using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddCommissionGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CommissionGroupId",
                table: "Employees",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommissionGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Basis = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DefaultRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TargetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CommissionSchemeId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommissionGroups_CommissionSchemes_CommissionSchemeId",
                        column: x => x.CommissionSchemeId,
                        principalTable: "CommissionSchemes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommissionGroupTiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CommissionGroupId = table.Column<int>(type: "int", nullable: false),
                    MinAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionGroupTiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommissionGroupTiers_CommissionGroups_CommissionGroupId",
                        column: x => x.CommissionGroupId,
                        principalTable: "CommissionGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_CommissionGroupId",
                table: "Employees",
                column: "CommissionGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionGroups_CommissionSchemeId",
                table: "CommissionGroups",
                column: "CommissionSchemeId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionGroupTiers_CommissionGroupId",
                table: "CommissionGroupTiers",
                column: "CommissionGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_CommissionGroups_CommissionGroupId",
                table: "Employees",
                column: "CommissionGroupId",
                principalTable: "CommissionGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Employees_CommissionGroups_CommissionGroupId",
                table: "Employees");

            migrationBuilder.DropTable(
                name: "CommissionGroupTiers");

            migrationBuilder.DropTable(
                name: "CommissionGroups");

            migrationBuilder.DropIndex(
                name: "IX_Employees_CommissionGroupId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "CommissionGroupId",
                table: "Employees");
        }
    }
}
