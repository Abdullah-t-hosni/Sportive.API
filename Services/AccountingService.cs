using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using System.Collections.Concurrent;
using Sportive.API.DTOs;
using MK = Sportive.API.Utils.MappingKeys;

namespace Sportive.API.Services;

public interface IAccountingService
{
    Task PostSalesOrderAsync(Order order);
    Task PostSalesReturnAsync(Order order);
    Task PostPurchaseInvoiceAsync(PurchaseInvoice invoice);
    Task PostPurchaseReturnAsync(PurchaseInvoice invoice, decimal returnedSubTotal = 0, decimal returnedTaxAmount = 0, decimal returnedDiscountAmount = 0);
    Task PostPurchaseReturnAsync(PurchaseReturn pReturn);
    Task PostOrderPaymentAsync(Order order);
    Task PostOrderRefundAsync(Order order);
    Task PostPartialSalesReturnAsync(Order order, List<OrderItem> returnedItems, decimal refundAmount, int? refundAccountId = null);
    Task PostSupplierPaymentAsync(SupplierPayment payment);
    Task ReverseEntryAsync(int journalEntryId, string reason);
    Task<JournalEntry> PostManualEntryAsync(CreateJournalEntryDto dto, string? userId);
    Task<JournalEntry> UpdateManualEntryAsync(int id, UpdateJournalEntryDto dto, string? userId);
    Task PostReceiptVoucherAsync(ReceiptVoucher voucher, int? orderId = null);
    Task PostPaymentVoucherAsync(PaymentVoucher voucher);
    Task<string> GetMappedCashAccount(PaymentMethod method, OrderSource source, Dictionary<string, int?>? map = null);
    Task<decimal> GetAccountBalanceAsync(string code);
    Task<decimal> GetTodayDrawerBalanceAsync(string cashAccountCode);
    Task SyncAllOrdersAccountingAsync();
    Task SyncAllPaymentAccountingAsync();
    Task SyncAllPurchaseAccountingAsync();
    Task SyncAllEntityIdsAsync();
    Task ConsolidateSubAccountsToControlAsync();
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
    public Task PostSalesReturnAsync(Order order) => _sales.PostSalesReturnAsync(order);
    public Task PostPartialSalesReturnAsync(Order order, List<OrderItem> returnedItems, decimal refundAmount, int? refundAccountId = null) 
        => _sales.PostPartialSalesReturnAsync(order, returnedItems, refundAmount, refundAccountId);

    public Task PostPurchaseInvoiceAsync(PurchaseInvoice invoice) => _purchase.PostPurchaseInvoiceAsync(invoice);
    public Task PostPurchaseReturnAsync(PurchaseInvoice invoice, decimal returnedSubTotal = 0, decimal returnedTaxAmount = 0, decimal returnedDiscountAmount = 0) => _purchase.PostPurchaseReturnAsync(invoice, returnedSubTotal, returnedTaxAmount, returnedDiscountAmount);
    public Task PostPurchaseReturnAsync(PurchaseReturn pReturn) => _purchase.PostPurchaseReturnAsync(pReturn);

    public Task PostOrderPaymentAsync(Order order) => _payment.PostOrderPaymentAsync(order);
    public Task PostOrderRefundAsync(Order order) => _payment.PostOrderRefundAsync(order);
    public Task PostReceiptVoucherAsync(ReceiptVoucher voucher, int? orderId = null) => _payment.PostReceiptVoucherAsync(voucher, orderId);
    public Task PostPaymentVoucherAsync(PaymentVoucher voucher) => _payment.PostPaymentVoucherAsync(voucher);
    public Task PostSupplierPaymentAsync(SupplierPayment payment) => _payment.PostSupplierPaymentAsync(payment);

    public Task ReverseEntryAsync(int journalEntryId, string reason) => _journal.ReverseEntryAsync(journalEntryId, reason);
    public Task<JournalEntry> PostManualEntryAsync(CreateJournalEntryDto dto, string? userId) => _journal.PostManualEntryAsync(dto, userId);
    public Task<JournalEntry> UpdateManualEntryAsync(int id, UpdateJournalEntryDto dto, string? userId) => _journal.UpdateManualEntryAsync(id, dto, userId);

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
        var todayStart = TimeHelper.GetEgyptTime().Date;
        return await _db.JournalLines.Where(l => l.AccountId == accountId && l.JournalEntry.EntryDate >= todayStart).SumAsync(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0;
    }

    public async Task SyncAllOrdersAccountingAsync()
    {
        var orders = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .Include(o => o.Payments)
            .Where(o => o.Status != OrderStatus.Cancelled)
            .ToListAsync();

        foreach (var order in orders)
        {
            try { await PostSalesOrderAsync(order); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to sync order {Number}", order.OrderNumber); }
        }
    }

    public async Task SyncAllPaymentAccountingAsync()
    {
        // 1. Receipt Vouchers
        var receipts = await _db.ReceiptVouchers.ToListAsync();
        foreach (var r in receipts)
        {
            try { await PostReceiptVoucherAsync(r, r.OrderId); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to sync receipt {Number}", r.VoucherNumber); }
        }

        // 2. Payment Vouchers
        var payments = await _db.PaymentVouchers.ToListAsync();
        foreach (var p in payments)
        {
            try { await PostPaymentVoucherAsync(p); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to sync payment {Number}", p.VoucherNumber); }
        }
    }

    public async Task SyncAllPurchaseAccountingAsync()
    {
        var invoices = await _db.PurchaseInvoices.Include(i => i.Supplier).Where(i => i.Status != PurchaseInvoiceStatus.Cancelled && i.Status != PurchaseInvoiceStatus.Draft).ToListAsync();
        foreach (var inv in invoices)
        {
            try { await PostPurchaseInvoiceAsync(inv); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to sync purchase {Number}", inv.InvoiceNumber); }
        }
    }

    public async Task SyncAllEntityIdsAsync()
    {
        _logger.LogInformation("SyncAllEntityIdsAsync called");
        await _core.SyncAllEntityIdsAsync();
    }

    public async Task ConsolidateSubAccountsToControlAsync()
    {
        _logger.LogInformation("ConsolidateSubAccountsToControlAsync called");
        await _core.ConsolidateSubAccountsToControlAsync();
    }
}
