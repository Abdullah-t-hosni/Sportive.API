using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddWapilotSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoSendWhatsAppInvoices",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WapilotApiKey",
                table: "StoreSettings",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "WapilotInstanceId",
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
                name: "AutoSendWhatsAppInvoices",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "WapilotApiKey",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "WapilotInstanceId",
                table: "StoreSettings");
        }
    }
}
