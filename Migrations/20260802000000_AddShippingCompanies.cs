using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace Sportive.API.Migrations
{
    public partial class AddShippingCompanies : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShippingCompanies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    NameAr = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NameEn = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContactInfo = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IntegrationType = table.Column<int>(type: "int", nullable: false),
                    ApiKey = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UseSandbox = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    AccountId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingCompanies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShippingCompanies_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "ShippingCompanyId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingCompanies_AccountId",
                table: "ShippingCompanies",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ShippingCompanyId",
                table: "Orders",
                column: "ShippingCompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_ShippingCompanies_ShippingCompanyId",
                table: "Orders",
                column: "ShippingCompanyId",
                principalTable: "ShippingCompanies",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_ShippingCompanies_ShippingCompanyId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "ShippingCompanies");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ShippingCompanyId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingCompanyId",
                table: "Orders");
        }
    }
}
