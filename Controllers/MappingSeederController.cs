using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class MappingSeederController : ControllerBase
{
    private readonly AppDbContext _db;

    public MappingSeederController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost("auto-link")]
    public async Task<IActionResult> AutoLink()
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
            { MappingKeys.SalesDiscount, new[] { "4104", "خصم مسموح", "Sales Discount" } },
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
            { MappingKeys.SalariesPayable, new[] { "2201", "رواتب مستحقة" } },
            { MappingKeys.EmployeeAdvances, new[] { "1201", "سلف", "Advances" } },
            { MappingKeys.EmployeeBonuses, new[] { "5202", "مكافآت" } },
            { MappingKeys.EmployeeDeductions, new[] { "4109", "جزاءات", "خصومات موظفين" } },
            { MappingKeys.DepreciationExpense, new[] { "5203", "إهلاك", "Depreciation" } },
            { MappingKeys.AccumulatedDepreciation, new[] { "1108", "مجمع إهلاك" } },
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
}
