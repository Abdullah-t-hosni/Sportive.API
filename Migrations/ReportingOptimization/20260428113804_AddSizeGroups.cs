using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportive.API.Migrations.ReportingOptimization
{
    /// <inheritdoc />
    public partial class AddSizeGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssetDepreciations_JournalEntries_JournalEntryId",
                table: "AssetDepreciations");

            migrationBuilder.DropForeignKey(
                name: "FK_AssetDisposals_Accounts_GainAccountId",
                table: "AssetDisposals");

            migrationBuilder.DropForeignKey(
                name: "FK_AssetDisposals_Accounts_LossAccountId",
                table: "AssetDisposals");

            migrationBuilder.DropForeignKey(
                name: "FK_AssetDisposals_Accounts_ProceedsAccountId",
                table: "AssetDisposals");

            migrationBuilder.DropForeignKey(
                name: "FK_AssetDisposals_JournalEntries_JournalEntryId",
                table: "AssetDisposals");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeAdvances_Accounts_CashAccountId",
                table: "EmployeeAdvances");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeAdvances_JournalEntries_JournalEntryId",
                table: "EmployeeAdvances");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeBonuses_Accounts_CashAccountId",
                table: "EmployeeBonuses");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeBonuses_JournalEntries_JournalEntryId",
                table: "EmployeeBonuses");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeBonuses_PayrollRuns_PayrollRunId",
                table: "EmployeeBonuses");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeDeductions_Accounts_CashAccountId",
                table: "EmployeeDeductions");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeDeductions_JournalEntries_JournalEntryId",
                table: "EmployeeDeductions");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeDeductions_PayrollRuns_PayrollRunId",
                table: "EmployeeDeductions");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssetCategories_Accounts_AccumDepreciationAccountId",
                table: "FixedAssetCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssetCategories_Accounts_AssetAccountId",
                table: "FixedAssetCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssetCategories_Accounts_DepreciationExpenseAccountId",
                table: "FixedAssetCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssets_Accounts_AccumDepreciationAccountId",
                table: "FixedAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssets_Accounts_AssetAccountId",
                table: "FixedAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssets_Accounts_DepreciationExpenseAccountId",
                table: "FixedAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssets_PurchaseInvoices_PurchaseInvoiceId",
                table: "FixedAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollRuns_Accounts_AccruedSalariesAccountId",
                table: "PayrollRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollRuns_Accounts_AdvancesAccountId",
                table: "PayrollRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollRuns_Accounts_DeductionRevenueAccountId",
                table: "PayrollRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollRuns_Accounts_WagesExpenseAccountId",
                table: "PayrollRuns");

            migrationBuilder.AddColumn<decimal>(
                name: "TotalCommunication",
                table: "PayrollRuns",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalTransportation",
                table: "PayrollRuns",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CommunicationAllowance",
                table: "PayrollItems",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransportationAllowance",
                table: "PayrollItems",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BonusAmount",
                table: "Employees",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CommunicationAllowance",
                table: "Employees",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransportationAllowance",
                table: "Employees",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "SizeGroupId",
                table: "Categories",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SizeGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SizeGroups", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SizeValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SizeGroupId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SizeValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SizeValues_SizeGroups_SizeGroupId",
                        column: x => x.SizeGroupId,
                        principalTable: "SizeGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_SizeGroupId",
                table: "Categories",
                column: "SizeGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SizeValues_SizeGroupId",
                table: "SizeValues",
                column: "SizeGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_AssetDepreciations_JournalEntries_JournalEntryId",
                table: "AssetDepreciations",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AssetDisposals_Accounts_GainAccountId",
                table: "AssetDisposals",
                column: "GainAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AssetDisposals_Accounts_LossAccountId",
                table: "AssetDisposals",
                column: "LossAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AssetDisposals_Accounts_ProceedsAccountId",
                table: "AssetDisposals",
                column: "ProceedsAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AssetDisposals_JournalEntries_JournalEntryId",
                table: "AssetDisposals",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_SizeGroups_SizeGroupId",
                table: "Categories",
                column: "SizeGroupId",
                principalTable: "SizeGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeAdvances_Accounts_CashAccountId",
                table: "EmployeeAdvances",
                column: "CashAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeAdvances_JournalEntries_JournalEntryId",
                table: "EmployeeAdvances",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeBonuses_Accounts_CashAccountId",
                table: "EmployeeBonuses",
                column: "CashAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeBonuses_JournalEntries_JournalEntryId",
                table: "EmployeeBonuses",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeBonuses_PayrollRuns_PayrollRunId",
                table: "EmployeeBonuses",
                column: "PayrollRunId",
                principalTable: "PayrollRuns",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeDeductions_Accounts_CashAccountId",
                table: "EmployeeDeductions",
                column: "CashAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeDeductions_JournalEntries_JournalEntryId",
                table: "EmployeeDeductions",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeDeductions_PayrollRuns_PayrollRunId",
                table: "EmployeeDeductions",
                column: "PayrollRunId",
                principalTable: "PayrollRuns",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssetCategories_Accounts_AccumDepreciationAccountId",
                table: "FixedAssetCategories",
                column: "AccumDepreciationAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssetCategories_Accounts_AssetAccountId",
                table: "FixedAssetCategories",
                column: "AssetAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssetCategories_Accounts_DepreciationExpenseAccountId",
                table: "FixedAssetCategories",
                column: "DepreciationExpenseAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssets_Accounts_AccumDepreciationAccountId",
                table: "FixedAssets",
                column: "AccumDepreciationAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssets_Accounts_AssetAccountId",
                table: "FixedAssets",
                column: "AssetAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssets_Accounts_DepreciationExpenseAccountId",
                table: "FixedAssets",
                column: "DepreciationExpenseAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssets_PurchaseInvoices_PurchaseInvoiceId",
                table: "FixedAssets",
                column: "PurchaseInvoiceId",
                principalTable: "PurchaseInvoices",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollRuns_Accounts_AccruedSalariesAccountId",
                table: "PayrollRuns",
                column: "AccruedSalariesAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollRuns_Accounts_AdvancesAccountId",
                table: "PayrollRuns",
                column: "AdvancesAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollRuns_Accounts_DeductionRevenueAccountId",
                table: "PayrollRuns",
                column: "DeductionRevenueAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollRuns_Accounts_WagesExpenseAccountId",
                table: "PayrollRuns",
                column: "WagesExpenseAccountId",
                principalTable: "Accounts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssetDepreciations_JournalEntries_JournalEntryId",
                table: "AssetDepreciations");

            migrationBuilder.DropForeignKey(
                name: "FK_AssetDisposals_Accounts_GainAccountId",
                table: "AssetDisposals");

            migrationBuilder.DropForeignKey(
                name: "FK_AssetDisposals_Accounts_LossAccountId",
                table: "AssetDisposals");

            migrationBuilder.DropForeignKey(
                name: "FK_AssetDisposals_Accounts_ProceedsAccountId",
                table: "AssetDisposals");

            migrationBuilder.DropForeignKey(
                name: "FK_AssetDisposals_JournalEntries_JournalEntryId",
                table: "AssetDisposals");

            migrationBuilder.DropForeignKey(
                name: "FK_Categories_SizeGroups_SizeGroupId",
                table: "Categories");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeAdvances_Accounts_CashAccountId",
                table: "EmployeeAdvances");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeAdvances_JournalEntries_JournalEntryId",
                table: "EmployeeAdvances");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeBonuses_Accounts_CashAccountId",
                table: "EmployeeBonuses");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeBonuses_JournalEntries_JournalEntryId",
                table: "EmployeeBonuses");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeBonuses_PayrollRuns_PayrollRunId",
                table: "EmployeeBonuses");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeDeductions_Accounts_CashAccountId",
                table: "EmployeeDeductions");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeDeductions_JournalEntries_JournalEntryId",
                table: "EmployeeDeductions");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeDeductions_PayrollRuns_PayrollRunId",
                table: "EmployeeDeductions");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssetCategories_Accounts_AccumDepreciationAccountId",
                table: "FixedAssetCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssetCategories_Accounts_AssetAccountId",
                table: "FixedAssetCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssetCategories_Accounts_DepreciationExpenseAccountId",
                table: "FixedAssetCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssets_Accounts_AccumDepreciationAccountId",
                table: "FixedAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssets_Accounts_AssetAccountId",
                table: "FixedAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssets_Accounts_DepreciationExpenseAccountId",
                table: "FixedAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssets_PurchaseInvoices_PurchaseInvoiceId",
                table: "FixedAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollRuns_Accounts_AccruedSalariesAccountId",
                table: "PayrollRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollRuns_Accounts_AdvancesAccountId",
                table: "PayrollRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollRuns_Accounts_DeductionRevenueAccountId",
                table: "PayrollRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollRuns_Accounts_WagesExpenseAccountId",
                table: "PayrollRuns");

            migrationBuilder.DropTable(
                name: "SizeValues");

            migrationBuilder.DropTable(
                name: "SizeGroups");

            migrationBuilder.DropIndex(
                name: "IX_Categories_SizeGroupId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "TotalCommunication",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "TotalTransportation",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "CommunicationAllowance",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "TransportationAllowance",
                table: "PayrollItems");

            migrationBuilder.DropColumn(
                name: "BonusAmount",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "CommunicationAllowance",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "TransportationAllowance",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "SizeGroupId",
                table: "Categories");

            migrationBuilder.AddForeignKey(
                name: "FK_AssetDepreciations_JournalEntries_JournalEntryId",
                table: "AssetDepreciations",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AssetDisposals_Accounts_GainAccountId",
                table: "AssetDisposals",
                column: "GainAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AssetDisposals_Accounts_LossAccountId",
                table: "AssetDisposals",
                column: "LossAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AssetDisposals_Accounts_ProceedsAccountId",
                table: "AssetDisposals",
                column: "ProceedsAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AssetDisposals_JournalEntries_JournalEntryId",
                table: "AssetDisposals",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeAdvances_Accounts_CashAccountId",
                table: "EmployeeAdvances",
                column: "CashAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeAdvances_JournalEntries_JournalEntryId",
                table: "EmployeeAdvances",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeBonuses_Accounts_CashAccountId",
                table: "EmployeeBonuses",
                column: "CashAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeBonuses_JournalEntries_JournalEntryId",
                table: "EmployeeBonuses",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeBonuses_PayrollRuns_PayrollRunId",
                table: "EmployeeBonuses",
                column: "PayrollRunId",
                principalTable: "PayrollRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeDeductions_Accounts_CashAccountId",
                table: "EmployeeDeductions",
                column: "CashAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeDeductions_JournalEntries_JournalEntryId",
                table: "EmployeeDeductions",
                column: "JournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeDeductions_PayrollRuns_PayrollRunId",
                table: "EmployeeDeductions",
                column: "PayrollRunId",
                principalTable: "PayrollRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssetCategories_Accounts_AccumDepreciationAccountId",
                table: "FixedAssetCategories",
                column: "AccumDepreciationAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssetCategories_Accounts_AssetAccountId",
                table: "FixedAssetCategories",
                column: "AssetAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssetCategories_Accounts_DepreciationExpenseAccountId",
                table: "FixedAssetCategories",
                column: "DepreciationExpenseAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssets_Accounts_AccumDepreciationAccountId",
                table: "FixedAssets",
                column: "AccumDepreciationAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssets_Accounts_AssetAccountId",
                table: "FixedAssets",
                column: "AssetAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssets_Accounts_DepreciationExpenseAccountId",
                table: "FixedAssets",
                column: "DepreciationExpenseAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssets_PurchaseInvoices_PurchaseInvoiceId",
                table: "FixedAssets",
                column: "PurchaseInvoiceId",
                principalTable: "PurchaseInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollRuns_Accounts_AccruedSalariesAccountId",
                table: "PayrollRuns",
                column: "AccruedSalariesAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollRuns_Accounts_AdvancesAccountId",
                table: "PayrollRuns",
                column: "AdvancesAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollRuns_Accounts_DeductionRevenueAccountId",
                table: "PayrollRuns",
                column: "DeductionRevenueAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollRuns_Accounts_WagesExpenseAccountId",
                table: "PayrollRuns",
                column: "WagesExpenseAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
