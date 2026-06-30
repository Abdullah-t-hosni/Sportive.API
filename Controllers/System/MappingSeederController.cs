using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.Mapping, requireEdit: true)]
public class MappingSeederController : ControllerBase
{
    private readonly AppDbContext _db;

    public MappingSeederController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost("seed-chart-of-accounts")]
    [Authorize(Policy = "AdminOnly")] // Secured to prevent unauthorized access
    public async Task<IActionResult> SeedChartOfAccounts()
    {
        // 1. Safety check: Only seed if database has NO accounts to prevent overriding existing clients
        if (await _db.Accounts.AnyAsync())
        {
            return BadRequest(new { message = "Chart of accounts is already seeded. Cannot re-seed." });
        }

        try
        {
            // Seed hierarchical accounts
            // Level 1 Parents
            var assets = new Account { Code = "1", NameAr = "الأصول", NameEn = "Assets", Type = AccountType.Asset, Nature = AccountNature.Debit, Level = 1, IsLeaf = false, AllowPosting = false, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() };
            var liabilities = new Account { Code = "2", NameAr = "الخصوم", NameEn = "Liabilities", Type = AccountType.Liability, Nature = AccountNature.Credit, Level = 1, IsLeaf = false, AllowPosting = false, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() };
            var equity = new Account { Code = "3", NameAr = "حقوق الملكية", NameEn = "Equity", Type = AccountType.Equity, Nature = AccountNature.Credit, Level = 1, IsLeaf = false, AllowPosting = false, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() };
            var revenue = new Account { Code = "4", NameAr = "الإيرادات", NameEn = "Revenue", Type = AccountType.Revenue, Nature = AccountNature.Credit, Level = 1, IsLeaf = false, AllowPosting = false, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() };
            var expenses = new Account { Code = "5", NameAr = "المصروفات", NameEn = "Expenses", Type = AccountType.Expense, Nature = AccountNature.Debit, Level = 1, IsLeaf = false, AllowPosting = false, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() };

            _db.Accounts.AddRange(assets, liabilities, equity, revenue, expenses);
            await _db.SaveChangesAsync();

            // Level 2 Sub-parents
            var currentAssets = new Account { Code = "11", NameAr = "الأصول المتداولة", NameEn = "Current Assets", Type = AccountType.Asset, Nature = AccountNature.Debit, Level = 2, ParentId = assets.Id, IsLeaf = false, AllowPosting = false, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() };
            var nonCurrentAssets = new Account { Code = "12", NameAr = "الأصول غير المتداولة", NameEn = "Non-Current Assets", Type = AccountType.Asset, Nature = AccountNature.Debit, Level = 2, ParentId = assets.Id, IsLeaf = false, AllowPosting = false, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() };
            var currentLiabilities = new Account { Code = "21", NameAr = "الخصوم المتداولة", NameEn = "Current Liabilities", Type = AccountType.Liability, Nature = AccountNature.Credit, Level = 2, ParentId = liabilities.Id, IsLeaf = false, AllowPosting = false, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() };
            var operatingExpenses = new Account { Code = "51", NameAr = "المصروفات التشغيلية", NameEn = "Operating Expenses", Type = AccountType.Expense, Nature = AccountNature.Debit, Level = 2, ParentId = expenses.Id, IsLeaf = false, AllowPosting = false, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() };
            var adminExpenses = new Account { Code = "52", NameAr = "المصروفات العمومية والإدارية", NameEn = "Administrative & General Expenses", Type = AccountType.Expense, Nature = AccountNature.Debit, Level = 2, ParentId = expenses.Id, IsLeaf = false, AllowPosting = false, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() };

            _db.Accounts.AddRange(currentAssets, nonCurrentAssets, currentLiabilities, operatingExpenses, adminExpenses);
            await _db.SaveChangesAsync();

            // Level 3 Concrete posting accounts (Leafs)
            var concreteAccounts = new List<Account>
            {
                // Assets
                new Account { Code = "1101", NameAr = "الصندوق الرئيسي", NameEn = "Main Cash Drawer", Type = AccountType.Asset, Nature = AccountNature.Debit, Level = 3, ParentId = currentAssets.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime(), CanReceivePayment = true },
                new Account { Code = "1102", NameAr = "حساب البنك / فيزا", NameEn = "Bank / Visa Account", Type = AccountType.Asset, Nature = AccountNature.Debit, Level = 3, ParentId = currentAssets.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime(), CanReceivePayment = true },
                new Account { Code = "1103", NameAr = "خزينة الكاشير POS", NameEn = "POS Cash Drawer", Type = AccountType.Asset, Nature = AccountNature.Debit, Level = 3, ParentId = currentAssets.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime(), CanReceivePayment = true },
                new Account { Code = "1104", NameAr = "حساب العملاء (مدينون)", NameEn = "Accounts Receivable", Type = AccountType.Asset, Nature = AccountNature.Debit, Level = 3, ParentId = currentAssets.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "1105", NameAr = "محفظة فودافون كاش", NameEn = "Vodafone Cash Wallet", Type = AccountType.Asset, Nature = AccountNature.Debit, Level = 3, ParentId = currentAssets.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime(), CanReceivePayment = true },
                new Account { Code = "1106", NameAr = "مخزون البضائع", NameEn = "Inventory Asset", Type = AccountType.Asset, Nature = AccountNature.Debit, Level = 3, ParentId = currentAssets.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "1107", NameAr = "حساب انستاباي", NameEn = "InstaPay Wallet", Type = AccountType.Asset, Nature = AccountNature.Debit, Level = 3, ParentId = currentAssets.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime(), CanReceivePayment = true },
                new Account { Code = "1108", NameAr = "مجمع إهلاك الأصول الثابتة", NameEn = "Accumulated Depreciation", Type = AccountType.Asset, Nature = AccountNature.Credit, Level = 3, ParentId = nonCurrentAssets.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "1109", NameAr = "ضريبة المدخلات (المشتريات)", NameEn = "Input VAT", Type = AccountType.Asset, Nature = AccountNature.Debit, Level = 3, ParentId = currentAssets.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "1201", NameAr = "سلف الموظفين", NameEn = "Employee Advances", Type = AccountType.Asset, Nature = AccountNature.Debit, Level = 3, ParentId = nonCurrentAssets.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },

                // Liabilities
                new Account { Code = "2101", NameAr = "حساب الموردين (دائنون)", NameEn = "Accounts Payable", Type = AccountType.Liability, Nature = AccountNature.Credit, Level = 3, ParentId = currentLiabilities.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "2103", NameAr = "الرواتب والأجور المستحقة", NameEn = "Salaries Payable", Type = AccountType.Liability, Nature = AccountNature.Credit, Level = 3, ParentId = currentLiabilities.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "2104", NameAr = "ضريبة المخرجات (المبيعات)", NameEn = "Output VAT", Type = AccountType.Liability, Nature = AccountNature.Credit, Level = 3, ParentId = currentLiabilities.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },

                // Equity
                new Account { Code = "3103", NameAr = "رأس المال الافتتاحي", NameEn = "Opening Balance Equity", Type = AccountType.Equity, Nature = AccountNature.Credit, Level = 3, ParentId = equity.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },

                // Revenue
                new Account { Code = "4101", NameAr = "إيرادات المبيعات", NameEn = "Sales Revenue", Type = AccountType.Revenue, Nature = AccountNature.Credit, Level = 3, ParentId = revenue.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "4102", NameAr = "مرتجع مبيعات", NameEn = "Sales Returns", Type = AccountType.Revenue, Nature = AccountNature.Debit, Level = 3, ParentId = revenue.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "4104", NameAr = "الخصم المسموح به", NameEn = "Sales Discount", Type = AccountType.Revenue, Nature = AccountNature.Debit, Level = 3, ParentId = revenue.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "4109", NameAr = "جزاءات وخصومات الموظفين", NameEn = "Employee Penalties Revenue", Type = AccountType.Revenue, Nature = AccountNature.Credit, Level = 3, ParentId = revenue.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },

                // Expenses (Operating)
                new Account { Code = "5101", NameAr = "تكلفة البضاعة المباعة (COGS)", NameEn = "Cost of Goods Sold", Type = AccountType.Expense, Nature = AccountNature.Debit, Level = 3, ParentId = operatingExpenses.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "5102", NameAr = "حساب المشتريات", NameEn = "Purchases", Type = AccountType.Expense, Nature = AccountNature.Debit, Level = 3, ParentId = operatingExpenses.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "5103", NameAr = "مرتجع مشتريات", NameEn = "Purchase Returns", Type = AccountType.Expense, Nature = AccountNature.Credit, Level = 3, ParentId = operatingExpenses.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "5105", NameAr = "الخصم المكتسب", NameEn = "Purchase Discount", Type = AccountType.Expense, Nature = AccountNature.Credit, Level = 3, ParentId = operatingExpenses.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },

                // Expenses (Admin/General)
                new Account { Code = "5201", NameAr = "مصروف فروقات الجرد", NameEn = "Inventory Variance Expense", Type = AccountType.Expense, Nature = AccountNature.Debit, Level = 3, ParentId = adminExpenses.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "5202", NameAr = "مصروف الرواتب والأجور", NameEn = "Salaries & Wages Expense", Type = AccountType.Expense, Nature = AccountNature.Debit, Level = 3, ParentId = adminExpenses.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "5203", NameAr = "مصروف إهلاك الأصول", NameEn = "Asset Depreciation Expense", Type = AccountType.Expense, Nature = AccountNature.Debit, Level = 3, ParentId = adminExpenses.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "5204", NameAr = "مصروف بدل انتقال", NameEn = "Transportation Allowance Expense", Type = AccountType.Expense, Nature = AccountNature.Debit, Level = 3, ParentId = adminExpenses.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "5205", NameAr = "مصروف بدل اتصالات", NameEn = "Communication Allowance Expense", Type = AccountType.Expense, Nature = AccountNature.Debit, Level = 3, ParentId = adminExpenses.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "5206", NameAr = "مصروف بدلات ثابتة", NameEn = "Fixed Allowance Expense", Type = AccountType.Expense, Nature = AccountNature.Debit, Level = 3, ParentId = adminExpenses.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "51202", NameAr = "مصروف عمولات البيع", NameEn = "Sales Commissions Expense", Type = AccountType.Expense, Nature = AccountNature.Debit, Level = 3, ParentId = adminExpenses.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new Account { Code = "51203", NameAr = "مصروف إضافي الموظفين", NameEn = "Overtime Expense", Type = AccountType.Expense, Nature = AccountNature.Debit, Level = 3, ParentId = adminExpenses.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime() }
            };

            _db.Accounts.AddRange(concreteAccounts);
            await _db.SaveChangesAsync();

            // 3. Trigger auto-link mapping immediately
            var mappingResult = await AutoLinkInternal();

            return Ok(new { 
                success = true, 
                message = "Default Chart of Accounts seeded and linked successfully.",
                accountsCount = 5 + 5 + concreteAccounts.Count,
                mappingDetails = mappingResult
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Error seeding Chart of Accounts", error = ex.Message });
        }
    }

    private async Task<List<string>> AutoLinkInternal()
    {
        var accounts = await _db.Accounts.ToListAsync();
        var results = new List<string>();

        var searchMap = new Dictionary<string, string[]>
        {
            { MappingKeys.Sales, new[] { "4101", "مبيعات", "Sales" } },
            { MappingKeys.Customer, new[] { "1104", "عملاء", "Customers", "Receivable" } },
            { MappingKeys.COGS, new[] { "5101", "تكلفة البضاعة", "COGS" } },
            { MappingKeys.Inventory, new[] { "1106", "مخزون", "Inventory" } },
            { MappingKeys.SalesDiscount, new[] { "4104", "خصم مسموح به", "Sales Discount" } },
            { MappingKeys.SalesReturn, new[] { "4102", "مرتجع مبيعات", "Sales Return" } },
            { MappingKeys.VatOutput, new[] { "2104", "ضريبة القيمة المضافة", "Output VAT" } },
            { MappingKeys.Supplier, new[] { "2101", "موردين", "Suppliers", "Payable" } },
            { MappingKeys.Purchase, new[] { "5102", "مشتريات", "Purchases" } },
            { MappingKeys.PurchaseDiscount, new[] { "5105", "خصم مكتسب", "Purchase Discount" } },
            { MappingKeys.PurchaseReturn, new[] { "5103", "مرتجع مشتريات", "Purchase Return" } },
            { MappingKeys.VatInput, new[] { "1109", "ضريبة مدخلات", "Input VAT" } },
            { MappingKeys.Cash, new[] { "1101", "صندوق", "الخزينة", "Cash" } },
            { MappingKeys.PaymentVoucherCash, new[] { "1101", "صندوق", "الخزينة", "Cash" } },
            { MappingKeys.PosCash, new[] { "1103", "كاشير", "POS Cash" } },
            { MappingKeys.PosBank, new[] { "1102", "بنك", "فيزا", "Bank" } },
            { MappingKeys.PosVodafone, new[] { "فودافون", "Vodafone" } },
            { MappingKeys.PosInstaPay, new[] { "انستاباي", "Instapay" } },
            { MappingKeys.WebCash, new[] { "1101", "صندوق", "الخزينة", "Cash" } },
            { MappingKeys.WebBank, new[] { "1102", "بنك", "فيزا", "Bank" } },
            { MappingKeys.WebVodafone, new[] { "فودافون", "Vodafone" } },
            { MappingKeys.WebInstaPay, new[] { "انستاباي", "Instapay" } },
            { MappingKeys.SalaryExpense, new[] { "5202", "رواتب", " wages", "Salaries" } },
            { MappingKeys.OvertimeExpense, new[] { "51203", "5202", "إضافي", "Overtime" } },
            { MappingKeys.SalesCommissionExpense, new[] { "51202", "عمولات بيع", "عمولات البيع", "Commission" } },
            { MappingKeys.SalariesPayable, new[] { "2103", "رواتب مستحقة - موظفين", "رواتب مستحقة موظفين", "رواتب مستحقة" } },
            { MappingKeys.EmployeeAdvances, new[] { "1201", "سلف", "Advances" } },
            { MappingKeys.EmployeeBonuses, new[] { "5202", "مكافآت" } },
            { MappingKeys.EmployeeDeductions, new[] { "4109", "جزاءات", "خصومات موظفين" } },
            { MappingKeys.DepreciationExpense, new[] { "5203", "إهلاك", "Depreciation" } },
            { MappingKeys.AccumulatedDepreciation, new[] { "1108", "مجمع إهلاك" } },
            { MappingKeys.TransportationAllowanceExpense, new[] { "5204", "انتقال", "بدل انتقال", "Transportation" } },
            { MappingKeys.CommunicationAllowanceExpense, new[] { "5205", "اتصال", "بدل اتصال", "Communication" } },
            { MappingKeys.FixedAllowanceExpense, new[] { "5206", "بدلات ثابتة", "Fixed Allowances" } },
            { MappingKeys.OpeningEquity, new[] { "3103", "افتتاحي", "Opening Balance" } },
            { MappingKeys.InventoryVariance, new[] { "5201", "فروقات جرد", "Inventory Variance" } }
        };

        foreach (var item in searchMap)
        {
            var match = accounts.FirstOrDefault(a => 
                item.Value.Any(keyword => 
                    (a.Code != null && a.Code.Equals(keyword, StringComparison.OrdinalIgnoreCase)) || 
                    (a.NameAr != null && a.NameAr.Contains(keyword, StringComparison.OrdinalIgnoreCase)) || 
                    (a.NameEn != null && a.NameEn.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                )
            );

            if (match != null)
            {
                var existing = await _db.AccountSystemMappings.FirstOrDefaultAsync(m => m.Key == item.Key);
                if (existing == null)
                {
                    _db.AccountSystemMappings.Add(new AccountSystemMapping { Key = item.Key, AccountId = match.Id });
                    results.Add($"Linked {item.Key} -> {match.Code}");
                }
                else
                {
                    existing.AccountId = match.Id;
                    results.Add($"Refreshed {item.Key} -> {match.Code}");
                }
            }
        }

        await _db.SaveChangesAsync();
        return results;
    }

    [HttpPost("auto-link")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AutoLink()
    {
        try
        {
            var accounts = await _db.Accounts.ToListAsync();
            var results = new List<string>();

            // قاموس للبحث الذكي: مفتاح الربط -> كلمات مفتاحية أو أكواد محتملة
            var searchMap = new Dictionary<string, string[]>
            {
                { MappingKeys.Sales, new[] { "4101", "مبيعات", "Sales" } },
                { MappingKeys.Customer, new[] { "1104", "عملاء", "Customers", "Receivable" } },
                { MappingKeys.COGS, new[] { "5101", "تكلفة البضاعة", "COGS" } },
                { MappingKeys.Inventory, new[] { "1106", "مخزون", "Inventory" } },
                { MappingKeys.SalesDiscount, new[] { "4104", "خصم مسموح به", "Sales Discount" } },
                { MappingKeys.SalesReturn, new[] { "4102", "مرتجع مبيعات", "Sales Return" } },
                { MappingKeys.VatOutput, new[] { "2104", "ضريبة القيمة المضافة", "Output VAT" } },
                { MappingKeys.Supplier, new[] { "2101", "موردين", "Suppliers", "Payable" } },
                { MappingKeys.Purchase, new[] { "5102", "مشتريات", "Purchases" } },
                { MappingKeys.PurchaseDiscount, new[] { "5105", "خصم مكتسب", "Purchase Discount" } },
                { MappingKeys.PurchaseReturn, new[] { "5103", "مرتجع مشتريات", "Purchase Return" } },
                { MappingKeys.VatInput, new[] { "1109", "ضريبة مدخلات", "Input VAT" } },
                { MappingKeys.Cash, new[] { "1101", "صندوق", "الخزينة", "Cash" } },
                { MappingKeys.PaymentVoucherCash, new[] { "1101", "صندوق", "الخزينة", "Cash" } },
                { MappingKeys.PosCash, new[] { "1103", "كاشير", "POS Cash" } },
                { MappingKeys.PosBank, new[] { "1102", "بنك", "فيزا", "Bank" } },
                { MappingKeys.PosVodafone, new[] { "فودافون", "Vodafone" } },
                { MappingKeys.PosInstaPay, new[] { "انستاباي", "Instapay" } },
                { MappingKeys.WebCash, new[] { "1101", "صندوق", "الخزينة", "Cash" } },
                { MappingKeys.WebBank, new[] { "1102", "بنك", "فيزا", "Bank" } },
                { MappingKeys.WebVodafone, new[] { "فودافون", "Vodafone" } },
                { MappingKeys.WebInstaPay, new[] { "انستاباي", "Instapay" } },
                { MappingKeys.SalaryExpense, new[] { "5202", "رواتب", " wages", "Salaries" } },
                { MappingKeys.OvertimeExpense, new[] { "51203", "5202", "إضافي", "Overtime" } },
                { MappingKeys.SalesCommissionExpense, new[] { "51202", "عمولات بيع", "عمولات البيع", "Commission" } },
                { MappingKeys.SalariesPayable, new[] { "2103", "رواتب مستحقة - موظفين", "رواتب مستحقة موظفين", "رواتب مستحقة" } },
                { MappingKeys.EmployeeAdvances, new[] { "1201", "سلف", "Advances" } },
                { MappingKeys.EmployeeBonuses, new[] { "5202", "مكافآت" } },
                { MappingKeys.EmployeeDeductions, new[] { "4109", "جزاءات", "خصومات موظفين" } },
                { MappingKeys.DepreciationExpense, new[] { "5203", "إهلاك", "Depreciation" } },
                { MappingKeys.AccumulatedDepreciation, new[] { "1108", "مجمع إهلاك" } },
                { MappingKeys.TransportationAllowanceExpense, new[] { "5204", "انتقال", "بدل انتقال", "Transportation" } },
                { MappingKeys.CommunicationAllowanceExpense, new[] { "5205", "اتصال", "بدل اتصال", "Communication" } },
                { MappingKeys.FixedAllowanceExpense, new[] { "5206", "بدلات ثابتة", "Fixed Allowances" } },
                { MappingKeys.OpeningEquity, new[] { "3103", "افتتاحي", "Opening Balance" } },
                { MappingKeys.InventoryVariance, new[] { "5201", "فروقات جرد", "Inventory Variance" } }
            };

            foreach (var item in searchMap)
            {
                var match = accounts.FirstOrDefault(a => 
                    item.Value.Any(keyword => 
                        (a.Code != null && a.Code.Equals(keyword, StringComparison.OrdinalIgnoreCase)) || 
                        (a.NameAr != null && a.NameAr.Contains(keyword, StringComparison.OrdinalIgnoreCase)) || 
                        (a.NameEn != null && a.NameEn.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    )
                );

                if (match != null)
                {
                    var existing = await _db.AccountSystemMappings.FirstOrDefaultAsync(m => m.Key == item.Key);
                    if (existing == null)
                    {
                        _db.AccountSystemMappings.Add(new AccountSystemMapping { Key = item.Key, AccountId = match.Id });
                        results.Add($"Linked {item.Key} -> {match.Code}");
                    }
                    else
                    {
                        existing.AccountId = match.Id;
                        results.Add($"Refreshed {item.Key} -> {match.Code}");
                    }
                }
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = "Auto-link completed", details = results });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Auto-link failed with exception", error = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    [HttpPost("force-link-overtime")]
    public async Task<IActionResult> ForceLinkOvertime()
    {
        var targetCode = "51203";
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == targetCode);
        if (account == null)
            return BadRequest(new { message = $"Account with code {targetCode} not found." });

        var key = MappingKeys.OvertimeExpense;
        var mapping = await _db.AccountSystemMappings.FirstOrDefaultAsync(m => m.Key == key);
        
        if (mapping == null)
        {
            _db.AccountSystemMappings.Add(new AccountSystemMapping { Key = key, AccountId = account.Id });
        }
        else
        {
            mapping.AccountId = account.Id;
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = $"OvertimeExpense successfully linked to account {targetCode} ({account.NameAr})" });
    }
}

