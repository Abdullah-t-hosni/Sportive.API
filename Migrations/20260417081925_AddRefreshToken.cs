using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.DecimalFix
{
    /// <inheritdoc />
    public partial class AddRefreshToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductDiscounts_Products_ProductId",
                table: "ProductDiscounts");

            migrationBuilder.AlterColumn<int>(
                name: "ProductId",
                table: "ProductDiscounts",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "BrandId",
                table: "ProductDiscounts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "ProductDiscounts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "OrderItems",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalUnitPrice",
                table: "OrderItems",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "RefreshToken",
                table: "AspNetUsers",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "RefreshTokenExpiry",
                table: "AspNetUsers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductDiscounts_BrandId",
                table: "ProductDiscounts",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductDiscounts_CategoryId",
                table: "ProductDiscounts",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductDiscounts_Brands_BrandId",
                table: "ProductDiscounts",
                column: "BrandId",
                principalTable: "Brands",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductDiscounts_Categories_CategoryId",
                table: "ProductDiscounts",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductDiscounts_Products_ProductId",
                table: "ProductDiscounts",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductDiscounts_Brands_BrandId",
                table: "ProductDiscounts");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductDiscounts_Categories_CategoryId",
                table: "ProductDiscounts");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductDiscounts_Products_ProductId",
                table: "ProductDiscounts");

            migrationBuilder.DropIndex(
                name: "IX_ProductDiscounts_BrandId",
                table: "ProductDiscounts");

            migrationBuilder.DropIndex(
                name: "IX_ProductDiscounts_CategoryId",
                table: "ProductDiscounts");

            migrationBuilder.DropColumn(
                name: "BrandId",
                table: "ProductDiscounts");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "ProductDiscounts");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "OriginalUnitPrice",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "RefreshToken",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RefreshTokenExpiry",
                table: "AspNetUsers");

            migrationBuilder.AlterColumn<int>(
                name: "ProductId",
                table: "ProductDiscounts",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductDiscounts_Products_ProductId",
                table: "ProductDiscounts",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
