using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public class BackfillService : IBackfillService
{
    private readonly AppDbContext _db;
    private readonly IAccountingService _accounting;
    private readonly ILogger<BackfillService> _logger;

    public BackfillService(AppDbContext db, IAccountingService accounting, ILogger<BackfillService> logger)
    {
        _db = db;
        _accounting = accounting;
        _logger = logger;
    }

    public async Task<(int Total, int Success, int Failed, List<string> Errors)> PostMissingOrdersAsync()
    {
        var missingOrders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Where(o => o.Status != OrderStatus.Cancelled)
            .Where(o => !_db.JournalEntries.Any(e => e.OrderId == o.Id))
            .ToListAsync();

        int successCount = 0;
        var errors = new List<string>();

        foreach (var batch in missingOrders.Chunk(20))
        {
            foreach (var order in batch)
            {
                try 
                {
                    await _accounting.PostSalesOrderAsync(order);
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Backfill] Failed for order {OrderId} - {OrderNumber}", order.Id, order.OrderNumber);
                    errors.Add($"Order {order.OrderNumber}: {ex.Message}");
                }
            }
        }

        return (missingOrders.Count, successCount, errors.Count, errors);
    }

    public async Task<(int Total, int Success, int Failed, List<string> Errors)> PostMissingPurchasesAsync()
    {
        var missingInvoices = await _db.PurchaseInvoices
            .AsNoTracking()
            .Include(i => i.Supplier)
            .Where(i => i.Status != PurchaseInvoiceStatus.Draft)
            .Where(i => !_db.JournalEntries.Any(e => e.Type == JournalEntryType.PurchaseInvoice && e.Reference == i.InvoiceNumber))
            .ToListAsync();

        int successCount = 0;
        var errors = new List<string>();

        foreach (var batch in missingInvoices.Chunk(20))
        {
            foreach (var inv in batch)
            {
                try 
                {
                    await _accounting.PostPurchaseInvoiceAsync(inv);
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Backfill] Failed for purchase invoice {InvoiceId} - {InvoiceNumber}", inv.Id, inv.InvoiceNumber);
                    errors.Add($"Invoice {inv.InvoiceNumber}: {ex.Message}");
                }
            }
        }

        return (missingInvoices.Count, successCount, errors.Count, errors);
    }
}
