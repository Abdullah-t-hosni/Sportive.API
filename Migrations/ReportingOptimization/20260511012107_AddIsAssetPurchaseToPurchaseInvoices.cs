using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddIsAssetPurchaseToPurchaseInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAssetPurchase",
                table: "PurchaseInvoices",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AssetName",
                table: "PurchaseInvoiceItems",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "CreatedAssetId",
                table: "PurchaseInvoiceItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FixedAssetCategoryId",
                table: "PurchaseInvoiceItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItems_CreatedAssetId",
                table: "PurchaseInvoiceItems",
                column: "CreatedAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItems_FixedAssetCategoryId",
                table: "PurchaseInvoiceItems",
                column: "FixedAssetCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoiceItems_FixedAssetCategories_FixedAssetCategory~",
                table: "PurchaseInvoiceItems",
                column: "FixedAssetCategoryId",
                principalTable: "FixedAssetCategories",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoiceItems_FixedAssets_CreatedAssetId",
                table: "PurchaseInvoiceItems",
                column: "CreatedAssetId",
                principalTable: "FixedAssets",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoiceItems_FixedAssetCategories_FixedAssetCategory~",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoiceItems_FixedAssets_CreatedAssetId",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceItems_CreatedAssetId",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceItems_FixedAssetCategoryId",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropColumn(
                name: "IsAssetPurchase",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "AssetName",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropColumn(
                name: "CreatedAssetId",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropColumn(
                name: "FixedAssetCategoryId",
                table: "PurchaseInvoiceItems");
        }
    }
}
