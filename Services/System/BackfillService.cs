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
            .Include(o => o.Items)
            .Include(o => o.Customer)
            .Include(o => o.Payments)
            .Where(o => o.Status != OrderStatus.Cancelled)
            .Where(o => !_db.JournalEntries.Any(e => (e.OrderId == o.Id || e.Reference == o.OrderNumber) && e.Type == JournalEntryType.SalesInvoice && e.Status != JournalEntryStatus.Reversed))
            .Take(50)
            .ToListAsync();

        if (!missingOrders.Any())
            return (0, 0, 0, new List<string>());

        int successCount = 0;
        var errors = new List<string>();

        foreach (var order in missingOrders)
        {
            try 
            {
                await _accounting.PostSalesOrderAsync(order);
                if (order.PaidAmount > 0)
                {
                    try { await _accounting.PostOrderPaymentAsync(order); } catch { }
                }
                successCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Backfill] Failed for order {OrderId} - {OrderNumber}", order.Id, order.OrderNumber);
                errors.Add($"Order {order.OrderNumber}: {ex.Message}");
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

    public async Task<(int Total, int Success, int Failed, List<string> Errors)> FixManualJournalEntriesEntityLinksAsync()
    {
        int successCount = 0;
        var errors = new List<string>();

        try
        {
            var lines = await _db.JournalLines
                .Include(l => l.Account)
                .Where(l => l.SupplierId == null && l.CustomerId == null && l.EmployeeId == null)
                .ToListAsync();

            var suppliers = await _db.Suppliers.Where(s => s.MainAccountId != null).ToListAsync();
            var supplierMap = suppliers.GroupBy(s => s.MainAccountId!.Value).ToDictionary(g => g.Key, g => g.First().Id);

            var customers = await _db.Customers.Where(c => c.MainAccountId != null).ToListAsync();
            var customerMap = customers.GroupBy(c => c.MainAccountId!.Value).ToDictionary(g => g.Key, g => g.First().Id);

            var employees = await _db.Employees.Where(e => e.AccountId != null).ToListAsync();
            var employeeMap = employees.GroupBy(e => e.AccountId!.Value).ToDictionary(g => g.Key, g => g.First().Id);

            foreach (var line in lines)
            {
                bool updated = false;

                if (supplierMap.TryGetValue(line.AccountId, out var sId))
                {
                    line.SupplierId = sId;
                    updated = true;
                }
                else if (customerMap.TryGetValue(line.AccountId, out var cId))
                {
                    line.CustomerId = cId;
                    updated = true;
                }
                else if (employeeMap.TryGetValue(line.AccountId, out var eId))
                {
                    line.EmployeeId = eId;
                    updated = true;
                }

                if (updated)
                {
                    successCount++;
                }
            }

            if (successCount > 0)
            {
                await _db.SaveChangesAsync();
                // trigger balance sync to apply updates
                Hangfire.BackgroundJob.Enqueue<IAccountingService>(a => a.SyncEntityBalancesAsync());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fixing journal entity links");
            errors.Add(ex.Message);
        }

        return (successCount, successCount, errors.Count, errors);
    }
}
