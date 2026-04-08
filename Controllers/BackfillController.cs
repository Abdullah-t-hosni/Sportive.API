using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class BackfillController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAccountingService _accounting;

    public BackfillController(AppDbContext db, IAccountingService accounting)
    {
        _db = db;
        _accounting = accounting;
    }

    /// <summary>
    /// يقوم بترحيل كافة الطلبات التي لم يتم ترحيلها محاسبياً بعد
    /// </summary>
    [HttpPost("post-missing-orders")]
    public async Task<IActionResult> PostMissingOrders()
    {
        // جلب أرقام الطلبات المرحلة مسبقاً من الـ JournalEntries
        var postedOrderIds = await _db.JournalEntries
            .Where(e => e.OrderId != null)
            .Select(e => e.OrderId!.Value)
            .Distinct()
            .ToListAsync();

        // جلب الطلبات غير المرحلة (باستثناء الملغاة)
        var missingOrders = await _db.Orders
            .Include(o => o.Customer)
            .Where(o => !postedOrderIds.Contains(o.Id) && o.Status != OrderStatus.Cancelled)
            .ToListAsync();

        int count = 0;
        foreach (var order in missingOrders)
        {
            try {
                await _accounting.PostSalesOrderAsync(order);
                count++;
            } catch { }
        }

        return Ok(new { message = $"Successfully posted {count} orders to accounting.", totalMissingFound = missingOrders.Count });
    }

    /// <summary>
    /// يقوم بترحيل كافة فواتير المشتريات التي لم يتم ترحيلها
    /// </summary>
    [HttpPost("post-missing-purchases")]
    public async Task<IActionResult> PostMissingPurchases()
    {
         // المشتريات متميزة بـ Reference أو عن طريق الـ Type
        var postedPurchases = await _db.JournalEntries
            .Where(e => e.Type == JournalEntryType.PurchaseInvoice && e.Reference != null)
            .Select(e => e.Reference)
            .ToListAsync();

        var missingInvoices = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Where(i => !postedPurchases.Contains(i.InvoiceNumber) && i.Status != PurchaseInvoiceStatus.Draft)
            .ToListAsync();

        int count = 0;
        foreach (var inv in missingInvoices)
        {
            try {
                await _accounting.PostPurchaseInvoiceAsync(inv);
                count++;
            } catch { }
        }

        return Ok(new { message = $"Successfully posted {count} purchases to accounting.", totalMissingFound = missingInvoices.Count });
    }
}
