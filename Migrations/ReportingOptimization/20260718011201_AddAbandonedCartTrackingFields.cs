using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddAbandonedCartTrackingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsShiftOverridden",
                table: "EmployeeAttendances",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AbandonedCartCouponCode",
                table: "Customers",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "AbandonedCartRecoveredAt",
                table: "Customers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AbandonedCartRecoveredOrderNumber",
                table: "Customers",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "AbandonedCartReminderSentAt",
                table: "Customers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AbandonedCartValue",
                table: "Customers",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAbandonedCartRecovered",
                table: "Customers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EmployeeShiftOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    OverrideDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DayOfWeek = table.Column<int>(type: "int", nullable: true),
                    ShiftStartTime = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WorkHoursPerDay = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    IsDayOff = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Notes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeShiftOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeShiftOverrides_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeShiftOverrides_EmployeeId",
                table: "EmployeeShiftOverrides",
                column: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeShiftOverrides");

            migrationBuilder.DropColumn(
                name: "IsShiftOverridden",
                table: "EmployeeAttendances");

            migrationBuilder.DropColumn(
                name: "AbandonedCartCouponCode",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "AbandonedCartRecoveredAt",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "AbandonedCartRecoveredOrderNumber",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "AbandonedCartReminderSentAt",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "AbandonedCartValue",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IsAbandonedCartRecovered",
                table: "Customers");
        }
    }
}
