using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddGraduatedDelayPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DelayGraceMinutes",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DelayHalfDayLimitMinutes",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DelayQuarterDayLimitMinutes",
                table: "StoreSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "EnableGraduatedDelayPolicy",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                columns: new[] { "DelayGraceMinutes", "DelayHalfDayLimitMinutes", "DelayQuarterDayLimitMinutes", "EnableGraduatedDelayPolicy" },
                values: new object[] { 15, 60, 30, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DelayGraceMinutes",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "DelayHalfDayLimitMinutes",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "DelayQuarterDayLimitMinutes",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "EnableGraduatedDelayPolicy",
                table: "StoreSettings");
        }
    }
}
