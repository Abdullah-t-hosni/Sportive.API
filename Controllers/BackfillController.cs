using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

/// <summary>
/// يُرحّل قيود الطلبات والمشتريات القديمة (قبل تفعيل الـ AutoPost)
/// استخدمه مرة واحدة فقط من Swagger
/// POST /api/backfill/orders
/// POST /api/backfill/purchases
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class BackfillController : ControllerBase
{
    private readonly AppDbContext       _db;
    private readonly IAccountingService _accounting;

    public BackfillController(AppDbContext db, IAccountingService accounting)
    {
        _db         = db;
        _accounting = accounting;
    }

    // POST /api/backfill/orders?dryRun=true
    [HttpPost("orders")]
    public async Task<IActionResult> BackfillOrders([FromQuery] bool dryRun = true)
    {
        var orders = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Where(o => !o.IsDeleted
                     && o.Status != OrderStatus.Cancelled
                     && !_db.JournalEntries.Any(e =>
                            e.Reference == o.OrderNumber
                         && e.Type == JournalEntryType.SalesInvoice
                         && !e.IsDeleted))
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();

        if (dryRun)
            return Ok(new {
                dryRun   = true,
                message  = $"سيتم ترحيل {orders.Count} طلب",
                orders   = orders.Select(o => new { o.OrderNumber, o.TotalAmount, o.CreatedAt })
            });

        int success = 0, failed = 0;
        var errors  = new List<string>();

        foreach (var order in orders)
        {
            try
            {
                await _accounting.PostSalesOrderAsync(order);
                success++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{order.OrderNumber}: {ex.Message}");
            }
        }

        return Ok(new { success, failed, errors });
    }

    // POST /api/backfill/purchases?dryRun=true
    [HttpPost("purchases")]
    public async Task<IActionResult> BackfillPurchases([FromQuery] bool dryRun = true)
    {
        var invoices = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Where(i => !i.IsDeleted
                     && !_db.JournalEntries.Any(e =>
                            e.Reference == i.InvoiceNumber
                         && e.Type == JournalEntryType.PurchaseInvoice
                         && !e.IsDeleted))
            .OrderBy(i => i.InvoiceDate)
            .ToListAsync();

        if (dryRun)
            return Ok(new {
                dryRun   = true,
                message  = $"سيتم ترحيل {invoices.Count} فاتورة مشتريات",
                invoices = invoices.Select(i => new { i.InvoiceNumber, i.TotalAmount, i.InvoiceDate })
            });

        int success = 0, failed = 0;
        var errors  = new List<string>();

        foreach (var inv in invoices)
        {
            try
            {
                await _accounting.PostPurchaseInvoiceAsync(inv);
                success++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{inv.InvoiceNumber}: {ex.Message}");
            }
        }

        return Ok(new { success, failed, errors });
    }
}
