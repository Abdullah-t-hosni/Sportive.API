using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddPOSShiftClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "POSShiftClosures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StationId = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClosureDate = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClosedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartingBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ExpectedCash = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ActualCash = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Variance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrossSales = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetSales = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CashSales = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CardSales = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VodafoneCashSales = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    InstapaySales = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WalletSales = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreditSales = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Expenses = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SafeDrops = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Returns = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Discounts = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    JournalEntryReference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_POSShiftClosures", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "POSShiftClosures");
        }
    }
}
