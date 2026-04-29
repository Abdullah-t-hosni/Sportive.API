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

    [HttpPost("auto-link")]
    public async Task<IActionResult> AutoLink()
    {
        var accounts = await _db.Accounts.ToListAsync();
        var results = new List<string>();

        // Ù‚Ø§Ù…ÙˆØ³ Ù„Ù„Ø¨Ø­Ø« Ø§Ù„Ø°ÙƒÙŠ: Ù…ÙØªØ§Ø­ Ø§Ù„Ø±Ø¨Ø· -> ÙƒÙ„Ù…Ø§Øª Ù…ÙØªØ§Ø­ÙŠØ© Ø£Ùˆ Ø£ÙƒÙˆØ§Ø¯ Ù…Ø­ØªÙ…Ù„Ø©
        var searchMap = new Dictionary<string, string[]>
        {
            { MappingKeys.Sales, new[] { "4101", "Ù…Ø¨ÙŠØ¹Ø§Øª", "Sales" } },
            { MappingKeys.Customer, new[] { "1104", "Ø¹Ù…Ù„Ø§Ø¡", "Customers", "Receivable" } },
            { MappingKeys.COGS, new[] { "5101", "ØªÙƒÙ„ÙØ© Ø§Ù„Ø¨Ø¶Ø§Ø¹Ø©", "COGS" } },
            { MappingKeys.Inventory, new[] { "1106", "Ù…Ø®Ø²ÙˆÙ†", "Inventory" } },
            { MappingKeys.SalesDiscount, new[] { "4104", "Ø®ØµÙ… Ù…Ø³Ù…ÙˆØ­", "Sales Discount" } },
            { MappingKeys.SalesReturn, new[] { "4102", "Ù…Ø±ØªØ¬Ø¹ Ù…Ø¨ÙŠØ¹Ø§Øª", "Sales Return" } },
            { MappingKeys.VatOutput, new[] { "2104", "Ø¶Ø±ÙŠØ¨Ø© Ø§Ù„Ù‚ÙŠÙ…Ø© Ø§Ù„Ù…Ø¶Ø§ÙØ©", "Output VAT" } },
            { MappingKeys.Supplier, new[] { "2101", "Ù…ÙˆØ±Ø¯ÙŠÙ†", "Suppliers", "Payable" } },
            { MappingKeys.Purchase, new[] { "5102", "Ù…Ø´ØªØ±ÙŠØ§Øª", "Purchases" } },
            { MappingKeys.PurchaseDiscount, new[] { "5105", "Ø®ØµÙ… Ù…ÙƒØªØ³Ø¨", "Purchase Discount" } },
            { MappingKeys.PurchaseReturn, new[] { "5103", "Ù…Ø±ØªØ¬Ø¹ Ù…Ø´ØªØ±ÙŠØ§Øª", "Purchase Return" } },
            { MappingKeys.VatInput, new[] { "1109", "Ø¶Ø±ÙŠØ¨Ø© Ù…Ø¯Ø®Ù„Ø§Øª", "Input VAT" } },
            { MappingKeys.Cash, new[] { "1101", "ØµÙ†Ø¯ÙˆÙ‚", "Ø§Ù„Ø®Ø²ÙŠÙ†Ø©", "Cash" } },
            { MappingKeys.PaymentVoucherCash, new[] { "1101", "ØµÙ†Ø¯ÙˆÙ‚", "Ø§Ù„Ø®Ø²ÙŠÙ†Ø©", "Cash" } },
            { MappingKeys.PosCash, new[] { "1103", "ÙƒØ§Ø´ÙŠØ±", "POS Cash" } },
            { MappingKeys.PosBank, new[] { "1102", "Ø¨Ù†Ùƒ", "ÙÙŠØ²Ø§", "Bank" } },
            { MappingKeys.PosVodafone, new[] { "ÙÙˆØ¯Ø§ÙÙˆÙ†", "Vodafone" } },
            { MappingKeys.PosInstaPay, new[] { "Ø§Ù†Ø³ØªØ§Ø¨Ø§ÙŠ", "Instapay" } },
            { MappingKeys.WebCash, new[] { "1101", "ØµÙ†Ø¯ÙˆÙ‚", "Ø§Ù„Ø®Ø²ÙŠÙ†Ø©", "Cash" } },
            { MappingKeys.WebBank, new[] { "1102", "Ø¨Ù†Ùƒ", "ÙÙŠØ²Ø§", "Bank" } },
            { MappingKeys.WebVodafone, new[] { "ÙÙˆØ¯Ø§ÙÙˆÙ†", "Vodafone" } },
            { MappingKeys.WebInstaPay, new[] { "Ø§Ù†Ø³ØªØ§Ø¨Ø§ÙŠ", "Instapay" } },
            { MappingKeys.SalaryExpense, new[] { "5202", "Ø±ÙˆØ§ØªØ¨", " wages", "Salaries" } },
            { MappingKeys.SalariesPayable, new[] { "2103", "Ø±ÙˆØ§ØªØ¨ Ù…Ø³ØªØ­Ù‚Ø© - Ù…ÙˆØ¸ÙÙŠÙ†", "Ø±ÙˆØ§ØªØ¨ Ù…Ø³ØªØ­Ù‚Ø© Ù…ÙˆØ¸ÙÙŠÙ†", "Ø±ÙˆØ§ØªØ¨ Ù…Ø³ØªØ­Ù‚Ø©" } },
            { MappingKeys.EmployeeAdvances, new[] { "1201", "Ø³Ù„Ù", "Advances" } },
            { MappingKeys.EmployeeBonuses, new[] { "5202", "Ù…ÙƒØ§ÙØ¢Øª" } },
            { MappingKeys.EmployeeDeductions, new[] { "4109", "Ø¬Ø²Ø§Ø¡Ø§Øª", "Ø®ØµÙˆÙ…Ø§Øª Ù…ÙˆØ¸ÙÙŠÙ†" } },
            { MappingKeys.DepreciationExpense, new[] { "5203", "Ø¥Ù‡Ù„Ø§Ùƒ", "Depreciation" } },
            { MappingKeys.AccumulatedDepreciation, new[] { "1108", "Ù…Ø¬Ù…Ø¹ Ø¥Ù‡Ù„Ø§Ùƒ" } },
            { MappingKeys.TransportationAllowanceExpense, new[] { "5204", "Ø§Ù†ØªÙ‚Ø§Ù„", "Ø¨Ø¯Ù„ Ø§Ù†ØªÙ‚Ø§Ù„", "Transportation" } },
            { MappingKeys.CommunicationAllowanceExpense, new[] { "5205", "Ø§ØªØµØ§Ù„", "Ø¨Ø¯Ù„ Ø§ØªØµØ§Ù„", "Communication" } },
            { MappingKeys.FixedAllowanceExpense, new[] { "5206", "Ø¨Ø¯Ù„Ø§Øª Ø«Ø§Ø¨ØªØ©", "Fixed Allowances" } },
            { MappingKeys.OpeningEquity, new[] { "3103", "Ø§ÙØªØªØ§Ø­ÙŠ", "Opening Balance" } },
            { MappingKeys.InventoryVariance, new[] { "5201", "ÙØ±ÙˆÙ‚Ø§Øª Ø¬Ø±Ø¯", "Inventory Variance" } }
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

