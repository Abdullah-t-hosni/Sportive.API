using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddCategorySortFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultSortBy",
                table: "Categories",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "DefaultSortDir",
                table: "Categories",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultSortBy",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "DefaultSortDir",
                table: "Categories");
        }
    }
}
