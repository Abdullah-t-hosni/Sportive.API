using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddDefaultLanguageToStoreInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultLanguage",
                table: "StoreSettings",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "EnableAccounting",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableECommerce",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableFixedAssets",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableHR",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnablePOS",
                table: "StoreSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "StoreSettings",
                keyColumn: "StoreConfigId",
                keyValue: 1,
                columns: new[] { "DefaultLanguage", "EnableAccounting", "EnableECommerce", "EnableFixedAssets", "EnableHR", "EnablePOS" },
                values: new object[] { "ar", true, true, true, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultLanguage",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "EnableAccounting",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "EnableECommerce",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "EnableFixedAssets",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "EnableHR",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "EnablePOS",
                table: "StoreSettings");
        }
    }
}
