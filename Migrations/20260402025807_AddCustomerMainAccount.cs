using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerMainAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MainAccountId",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_MainAccountId",
                table: "Customers",
                column: "MainAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Accounts_MainAccountId",
                table: "Customers",
                column: "MainAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Accounts_MainAccountId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_MainAccountId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "MainAccountId",
                table: "Customers");
        }
    }
}
