using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using System.Collections.Concurrent;
using Sportive.API.DTOs;
using System.Security.Claims;
using MK = Sportive.API.Utils.MappingKeys;

namespace Sportive.API.Services;

public interface IAccountingService
{
    Task PostSalesOrderAsync(Order order);
    Task PostSalesOrderByIdAsync(int orderId);
    Task PostSalesReturnAsync(Order order, int? refundAccountId = null);
    Task PostPurchaseInvoiceAsync(PurchaseInvoice invoice);
    Task PostPurchaseReturnAsync(PurchaseInvoice invoice, decimal returnedSubTotal = 0, decimal returnedTaxAmount = 0, decimal returnedDiscountAmount = 0);
    Task PostPurchaseReturnAsync(PurchaseReturn pReturn);
    Task PostOrderPaymentAsync(Order order);
    Task PostOrderPaymentByIdAsync(int orderId);
    Task PostOrderRefundAsync(Order order);
    Task PostPartialSalesReturnAsync(Order order, List<OrderItem> returnedItems, decimal refundAmount, int? refundAccountId = null, bool refundToStoreCredit = false);
    Task PostCostPriceAdjustmentAsync(Order order, decimal originalTotalAmount, decimal originalVatAmount, string refundMethod);
    Task PostSupplierPaymentAsync(SupplierPayment payment);
    Task PostDirectSalesReturnAsync(DirectReturnDto dto, string returnNumber, decimal totalCost);
    Task ReverseEntryAsync(int journalEntryId, string reason);
    Task<JournalEntry> PostManualEntryAsync(CreateJournalEntryDto dto, ClaimsPrincipal? user);
    Task<JournalEntry> UpdateManualEntryAsync(int id, UpdateJournalEntryDto dto, ClaimsPrincipal? user);
    Task PostReceiptVoucherAsync(ReceiptVoucher voucher, int? orderId = null);
    Task PostPaymentVoucherAsync(PaymentVoucher voucher);
    Task<string> GetMappedCashAccount(PaymentMethod method, OrderSource source, Dictionary<string, int?>? map = null);
    Task<decimal> GetAccountBalanceAsync(string code);
    Task<decimal> GetTodayDrawerBalanceAsync(string cashAccountCode);
    Task SyncAllOrdersAccountingAsync(int? daysLimit = null);
    Task SyncAllPaymentAccountingAsync();
    Task SyncAllPurchaseAccountingAsync(int? daysLimit = null);
    Task SyncAllEntityIdsAsync();
    Task SyncEntityBalancesAsync();
    Task ConsolidateSubAccountsToControlAsync();
    Task<int> PurgeInactiveSubAccountsAsync();
}

public class AccountingService : IAccountingService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AccountingService> _logger;
    private readonly AccountingCoreService _core;
    private readonly SalesAccountingService _sales;
    private readonly PurchaseAccountingService _purchase;
    private readonly PaymentAccountingService _payment;
    private readonly JournalAccountingService _journal;

    public AccountingService(
        AppDbContext db, 
        ILogger<AccountingService> logger,
        AccountingCoreService core,
        SalesAccountingService sales,
        PurchaseAccountingService purchase,
        PaymentAccountingService payment,
        JournalAccountingService journal)
    {
        _db = db;
        _logger = logger;
        _core = core;
        _sales = sales;
        _purchase = purchase;
        _payment = payment;
        _journal = journal;
    }

    public Task PostSalesOrderAsync(Order order) => _sales.PostSalesOrderAsync(order);
    public Task PostSalesReturnAsync(Order order, int? refundAccountId = null) => _sales.PostSalesReturnAsync(order, refundAccountId);
    public Task PostPartialSalesReturnAsync(Order order, List<OrderItem> returnedItems, decimal refundAmount, int? refundAccountId = null, bool refundToStoreCredit = false) 
        => _sales.PostPartialSalesReturnAsync(order, returnedItems, refundAmount, refundAccountId, refundToStoreCredit);

    public Task PostCostPriceAdjustmentAsync(Order order, decimal originalTotalAmount, decimal originalVatAmount, string refundMethod)
        => _sales.PostCostPriceAdjustmentAsync(order, originalTotalAmount, originalVatAmount, refundMethod);

    public Task PostDirectSalesReturnAsync(DirectReturnDto dto, string returnNumber, decimal totalCost)
        => _sales.PostDirectSalesReturnAsync(dto, returnNumber, totalCost);

    public Task PostPurchaseInvoiceAsync(PurchaseInvoice invoice) => _purchase.PostPurchaseInvoiceAsync(invoice);
    public Task PostPurchaseReturnAsync(PurchaseInvoice invoice, decimal returnedSubTotal = 0, decimal returnedTaxAmount = 0, decimal returnedDiscountAmount = 0) => _purchase.PostPurchaseReturnAsync(invoice, returnedSubTotal, returnedTaxAmount, returnedDiscountAmount);
    public Task PostPurchaseReturnAsync(PurchaseReturn pReturn) => _purchase.PostPurchaseReturnAsync(pReturn);

    public Task PostOrderPaymentAsync(Order order) => _payment.PostOrderPaymentAsync(order);

    public async Task PostSalesOrderByIdAsync(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order != null) await PostSalesOrderAsync(order);
    }

    public async Task PostOrderPaymentByIdAsync(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order != null) await PostOrderPaymentAsync(order);
    }
    public Task PostOrderRefundAsync(Order order) => _payment.PostOrderRefundAsync(order);
    public Task PostReceiptVoucherAsync(ReceiptVoucher voucher, int? orderId = null) => _payment.PostReceiptVoucherAsync(voucher, orderId);
    public Task PostPaymentVoucherAsync(PaymentVoucher voucher) => _payment.PostPaymentVoucherAsync(voucher);
    public Task PostSupplierPaymentAsync(SupplierPayment payment) => _payment.PostSupplierPaymentAsync(payment);

    public Task ReverseEntryAsync(int journalEntryId, string reason) => _journal.ReverseEntryAsync(journalEntryId, reason);
    public Task<JournalEntry> PostManualEntryAsync(CreateJournalEntryDto dto, ClaimsPrincipal? user) => _journal.PostManualEntryAsync(dto, user);
    public Task<JournalEntry> UpdateManualEntryAsync(int id, UpdateJournalEntryDto dto, ClaimsPrincipal? user) => _journal.UpdateManualEntryAsync(id, dto, user);

    public Task<string> GetMappedCashAccount(PaymentMethod method, OrderSource source, Dictionary<string, int?>? map = null) 
        => _core.GetMappedCashAccountAsync(method, source, map);

    public async Task<decimal> GetAccountBalanceAsync(string code)
    {
        var (accountId, _, _) = await _core.GetAccountIdAsync(code);
        return await _db.JournalLines.Where(l => l.AccountId == accountId).SumAsync(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0;
    }

    public async Task<decimal> GetTodayDrawerBalanceAsync(string cashAccountCode)
    {
        var (accountId, _, _) = await _core.GetAccountIdAsync(cashAccountCode);
        var bizStart = TimeHelper.GetEgyptBusinessDayStart();
        
        // 🚨 FIX: Filter by CostCenter == POS to isolate drawer cash from website online orders (COD)
        // Also use 2 AM business day start
        return await _db.JournalLines
            .Where(l => l.AccountId == accountId 
                     && l.JournalEntry.EntryDate >= bizStart 
                     && l.CostCenter == OrderSource.POS)
            .SumAsync(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0;
    }

    public async Task SyncAllOrdersAccountingAsync(int? daysLimit = null)
    {
        _logger.LogInformation("[Accounting] Starting Sync of Orders Accounting...");
        
        var query = _db.Orders
            .Where(o => o.Status != OrderStatus.Cancelled);

        if (daysLimit.HasValue)
        {
            var limitDate = TimeHelper.GetEgyptTime().AddDays(-daysLimit.Value);
            query = query.Where(o => o.CreatedAt >= limitDate);
        }

        var orderIds = await query
            .OrderByDescending(o => o.Id)
            .Select(o => o.Id)
            .ToListAsync();

        _logger.LogInformation("[Accounting] Found {Count} orders to sync.", orderIds.Count);

        int batchSize = 50;
        for (int i = 0; i < orderIds.Count; i += batchSize)
        {
            var batchIds = orderIds.Skip(i).Take(batchSize).ToList();
            var orders = await _db.Orders
                .Include(o => o.Customer)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Payments)
                .Where(o => batchIds.Contains(o.Id))
                .ToListAsync();

            foreach (var order in orders)
            {
                try { await PostSalesOrderAsync(order); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to sync order {Number}", order.OrderNumber); }
            }

            // Important: Clear change tracker to prevent memory bloat
            _db.ChangeTracker.Clear();
            _logger.LogInformation("[Accounting] Synced batch {Batch}/{Total}", i + orders.Count, orderIds.Count);
        }
        
        _logger.LogInformation("[Accounting] Full Sync Completed.");
    }

    public async Task SyncAllPaymentAccountingAsync()
    {
        _logger.LogInformation("[Accounting] Syncing All Payments/Receipts...");
        
        // 1. Receipt Vouchers
        var receiptIds = await _db.ReceiptVouchers.Select(r => r.Id).ToListAsync();
        int batchSize = 100;
        for (int i = 0; i < receiptIds.Count; i += batchSize)
        {
            var batchIds = receiptIds.Skip(i).Take(batchSize).ToList();
            var receipts = await _db.ReceiptVouchers.Where(r => batchIds.Contains(r.Id)).ToListAsync();
            foreach (var r in receipts)
            {
                try { await PostReceiptVoucherAsync(r, r.OrderId); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to sync receipt {Number}", r.VoucherNumber); }
            }
            _db.ChangeTracker.Clear();
        }

        // 2. Payment Vouchers
        var paymentIds = await _db.PaymentVouchers.Select(p => p.Id).ToListAsync();
        for (int i = 0; i < paymentIds.Count; i += batchSize)
        {
            var batchIds = paymentIds.Skip(i).Take(batchSize).ToList();
            var payments = await _db.PaymentVouchers.Where(p => batchIds.Contains(p.Id)).ToListAsync();
            foreach (var p in payments)
            {
                try { await PostPaymentVoucherAsync(p); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to sync payment {Number}", p.VoucherNumber); }
            }
            _db.ChangeTracker.Clear();
        }
    }

    public async Task SyncAllPurchaseAccountingAsync(int? daysLimit = null)
    {
        _logger.LogInformation("[Accounting] Syncing Purchase Invoices...");
        var query = _db.PurchaseInvoices
            .Where(i => i.Status != PurchaseInvoiceStatus.Cancelled && i.Status != PurchaseInvoiceStatus.Draft);

        if (daysLimit.HasValue)
        {
            var limitDate = TimeHelper.GetEgyptTime().AddDays(-daysLimit.Value);
            query = query.Where(i => i.InvoiceDate >= limitDate);
        }

        var invoiceIds = await query
            .Select(i => i.Id)
            .ToListAsync();

        int batchSize = 50;
        for (int i = 0; i < invoiceIds.Count; i += batchSize)
        {
            var batchIds = invoiceIds.Skip(i).Take(batchSize).ToList();
            var invoices = await _db.PurchaseInvoices
                .Include(i => i.Supplier)
                .Include(i => i.Payments)
                .Where(i => batchIds.Contains(i.Id))
                .ToListAsync();

            foreach (var inv in invoices)
            {
                try 
                { 
                    await PostPurchaseInvoiceAsync(inv); 
                    
                    // ✅ NEW: Also sync payments for this invoice
                    if (inv.Payments != null && inv.Payments.Any())
                    {
                        foreach (var p in inv.Payments)
                        {
                            await PostSupplierPaymentAsync(p);
                        }
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, "Failed to sync purchase {Number}", inv.InvoiceNumber); }
            }
            _db.ChangeTracker.Clear();
        }
    }

    public async Task SyncAllEntityIdsAsync()
    {
        _logger.LogInformation("SyncAllEntityIdsAsync called");
        await _core.SyncAllEntityIdsAsync();
    }

    public async Task SyncEntityBalancesAsync()
    {
        _logger.LogInformation("SyncEntityBalancesAsync called");
        await _core.SyncEntityBalancesAsync();
    }

    public async Task ConsolidateSubAccountsToControlAsync()
    {
        _logger.LogInformation("ConsolidateSubAccountsToControlAsync called");
        await _core.ConsolidateSubAccountsToControlAsync();
    }

    public async Task<int> PurgeInactiveSubAccountsAsync()
    {
        _logger.LogInformation("PurgeInactiveSubAccountsAsync called");
        return await _core.PurgeInactiveSubAccountsAsync();
    }
}
