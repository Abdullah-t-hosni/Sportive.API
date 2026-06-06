using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddCustomerEncryption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Phone",
                table: "Customers",
                newName: "PhoneHash");

            migrationBuilder.RenameIndex(
                name: "IX_Customers_Phone",
                table: "Customers",
                newName: "IX_Customers_PhoneHash");

            migrationBuilder.AddColumn<string>(
                name: "EmailEncrypted",
                table: "Customers",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "EmailHash",
                table: "Customers",
                type: "varchar(255)",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "EmailKeyVersion",
                table: "Customers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PhoneEncrypted",
                table: "Customers",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "PhoneKeyVersion",
                table: "Customers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_EmailHash",
                table: "Customers",
                column: "EmailHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_EmailHash",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "EmailEncrypted",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "EmailHash",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "EmailKeyVersion",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PhoneEncrypted",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PhoneKeyVersion",
                table: "Customers");

            migrationBuilder.RenameColumn(
                name: "PhoneHash",
                table: "Customers",
                newName: "Phone");

            migrationBuilder.RenameIndex(
                name: "IX_Customers_PhoneHash",
                table: "Customers",
                newName: "IX_Customers_Phone");
        }
    }
}
