using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddCommissionSchemes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CommissionSchemeId",
                table: "EmployeeCommissionSettings",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommissionSchemes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Basis = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DefaultRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TargetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionSchemes", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommissionSchemeTiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CommissionSchemeId = table.Column<int>(type: "int", nullable: false),
                    MinAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionSchemeTiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommissionSchemeTiers_CommissionSchemes_CommissionSchemeId",
                        column: x => x.CommissionSchemeId,
                        principalTable: "CommissionSchemes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeCommissionSettings_CommissionSchemeId",
                table: "EmployeeCommissionSettings",
                column: "CommissionSchemeId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionSchemeTiers_CommissionSchemeId",
                table: "CommissionSchemeTiers",
                column: "CommissionSchemeId");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeCommissionSettings_CommissionSchemes_CommissionSchem~",
                table: "EmployeeCommissionSettings",
                column: "CommissionSchemeId",
                principalTable: "CommissionSchemes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeCommissionSettings_CommissionSchemes_CommissionSchem~",
                table: "EmployeeCommissionSettings");

            migrationBuilder.DropTable(
                name: "CommissionSchemeTiers");

            migrationBuilder.DropTable(
                name: "CommissionSchemes");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeCommissionSettings_CommissionSchemeId",
                table: "EmployeeCommissionSettings");

            migrationBuilder.DropColumn(
                name: "CommissionSchemeId",
                table: "EmployeeCommissionSettings");
        }
    }
}
