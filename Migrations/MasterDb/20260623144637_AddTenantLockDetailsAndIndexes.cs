using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.MasterDb
{
    /// <inheritdoc />
    public partial class AddTenantLockDetailsAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAt",
                table: "Tenants",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LockedReason",
                table: "Tenants",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Name",
                table: "Plans",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Plans_Name",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "LockedAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LockedReason",
                table: "Tenants");
        }
    }
}
