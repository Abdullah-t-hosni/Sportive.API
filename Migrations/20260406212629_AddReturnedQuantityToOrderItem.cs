using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnedQuantityToOrderItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReturnedQuantity",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrderId",
                table: "JournalEntries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_OrderId",
                table: "JournalEntries",
                column: "OrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_Orders_OrderId",
                table: "JournalEntries",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntries_Orders_OrderId",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_OrderId",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "ReturnedQuantity",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "JournalEntries");
        }
    }
}
