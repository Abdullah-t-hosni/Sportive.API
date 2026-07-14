using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using Sportive.API.Interfaces;
using MK = Sportive.API.Utils.MappingKeys;

namespace Sportive.API.Services;

/// <summary>
/// خدمة مخصصة لسندات القبض والصرف وتحصيلات الطلبات
/// </summary>
public class PaymentAccountingService
{
    private readonly AppDbContext _db;
    private readonly AccountingCoreService _core;
    private readonly ILogger<PaymentAccountingService> _logger;
    private readonly ITranslator _t;
    private readonly IDashboardEventService _dashboardEvents;
 
    public PaymentAccountingService(AppDbContext db, AccountingCoreService core, ILogger<PaymentAccountingService> logger, ITranslator t, IDashboardEventService dashboardEvents)
    {
        _db = db;
        _core = core;
        _logger = logger;
        _t = t;
        _dashboardEvents = dashboardEvents;
    }

    public async Task PostOrderPaymentAsync(Order order, DateTime? overrideDate = null)
    {
        var reference = order.OrderNumber + "-PMT";
        if (await _core.EntryExistsAsync(JournalEntryType.ReceiptVoucher, reference)) return;

        // ✅ SMART SKIP: Only Website digital payment orders need a separate ReceiptVoucher.
        // For ALL other orders (POS cash, POS InstaPay, POS Vodafone, Website Cash/COD, Credit/آجل),
        // the payment is already embedded directly in the SalesInvoice — no separate PMT entry needed.
        //
        // This mirrors exactly the isWebsiteDigitalPayment logic in SalesAccountingService:
        // Website + (Vodafone | InstaPay | CreditCard | Bank) → SalesInvoice records full receivable debt,
        // then THIS method creates the ReceiptVoucher to settle it.
        // All other cases → SalesInvoice already debited the cash account directly → SKIP here.
        bool needsReceiptVoucher = order.Source == OrderSource.Website &&
            (order.PaymentMethod == PaymentMethod.Vodafone ||
             order.PaymentMethod == PaymentMethod.InstaPay  ||
             order.PaymentMethod == PaymentMethod.CreditCard ||
             order.PaymentMethod == PaymentMethod.Bank);

        if (!needsReceiptVoucher)
        {
            // Also check for Credit (آجل) orders that have been settled via a payment
            // i.e. original PaymentMethod was Credit but now PaidAmount > 0 and order has payments
            bool isCreditOrderWithPayments = order.PaymentMethod == PaymentMethod.Credit &&
                order.Payments != null && order.Payments.Any(p => p.Amount > 0 && p.Method != PaymentMethod.Credit);

            if (!isCreditOrderWithPayments)
            {
                _logger.LogInformation("[Accounting] Skipping separate ReceiptVoucher for order {OrderNum}; payment already in SalesInvoice (Source={Source}, Method={Method}).",
                    order.OrderNumber, order.Source, order.PaymentMethod);
                return;
            }
        }

        var mapDict = await _core.GetSafeSystemMappingsAsync();
        string receivablesAcct = order.Customer?.MainAccountId != null 
            ? $"ID:{order.Customer.MainAccountId}" 
            : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Customer, mapDict)}";
        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();
        decimal handledPaidAmt = 0;

        var payments = order.Payments?.Where(p => p.Amount > 0 && p.Method != PaymentMethod.Credit).ToList()
                    ?? new List<OrderPayment>();

        if (payments.Any())
        {
            foreach (var p in payments)
            {
                if (p.Method == PaymentMethod.CustomerBalance) continue;
                
                var code = await _core.GetMappedCashAccountAsync(p.Method, order.Source, mapDict);
                var methodLabel = _core.GetMethodLabel(p.Method);
                lines.Add((code, p.Amount, 0, _t.Get("Accounting.CollectionShortDesc", methodLabel, order.OrderNumber)));
                lines.Add((receivablesAcct, 0, p.Amount, _t.Get("Accounting.DebtClosureDesc", methodLabel, order.OrderNumber)));
                handledPaidAmt += p.Amount;
            }
        }
        else
        {
            var splits = _core.ParseMixedPayments(order.AdminNotes);
            if (splits.Count > 0)
            {
                foreach (var (m, v) in splits)
                {
                    if (m == PaymentMethod.CustomerBalance) continue;
                    
                    var code = await _core.GetMappedCashAccountAsync(m, order.Source, mapDict);
                    var methodLabel = _core.GetMethodLabel(m);
                    lines.Add((code, v, 0, _t.Get("Accounting.CollectionShortDesc", methodLabel, order.OrderNumber)));
                    lines.Add((receivablesAcct, 0, v, _t.Get("Accounting.DebtClosureDesc", methodLabel, order.OrderNumber)));
                    handledPaidAmt += v;
                }
            }
            else if (order.PaymentMethod != PaymentMethod.Credit && order.PaymentMethod != PaymentMethod.CustomerBalance && order.TotalAmount > 0)
            {
                var cashCode = await _core.GetMappedCashAccountAsync(order.PaymentMethod, order.Source, mapDict);
                var methodLabel = _core.GetMethodLabel(order.PaymentMethod);
                lines.Add((cashCode, order.TotalAmount, 0, _t.Get("Accounting.OrderCollectionDesc", order.OrderNumber, methodLabel)));
                lines.Add((receivablesAcct, 0, order.TotalAmount, _t.Get("Accounting.OrderDebtClosureDesc", order.OrderNumber)));
                handledPaidAmt = order.TotalAmount;
            }
        }

        if (!lines.Any()) return;
        
        var paymentDate = payments.Any() ? payments.Max(p => p.CreatedAt) : TimeHelper.GetEgyptTime();
        var entryDate = overrideDate ?? TimeHelper.GetEgyptBusinessDayDate(paymentDate);
        var createdDate = overrideDate ?? paymentDate;

        await _core.PostEntryAsync(JournalEntryType.ReceiptVoucher, reference, _t.Get("Accounting.AutoReceiptVoucherMainDesc", order.OrderNumber), entryDate, lines, orderId: order.Id, customerId: order.CustomerId, source: order.Source, createdAt: createdDate);
    }

    public async Task PostOrderRefundAsync(Order order)
    {
        var reference = order.OrderNumber + "-RFD";
        if (await _core.EntryExistsAsync(JournalEntryType.PaymentVoucher, reference)) return;

        var mapDict = await _core.GetSafeSystemMappingsAsync();
        string receivablesAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Customer, mapDict)}";
        
        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();
        
        if (order.PaymentMethod == PaymentMethod.CustomerBalance)
        {
            lines.Add((receivablesAcct, order.TotalAmount, 0, _t.Get("Accounting.DebtRefundDesc", order.OrderNumber)));
            lines.Add((receivablesAcct, 0, order.TotalAmount, "رد القيمة لرصيد العميل المتاح"));
        }
        else
        {
            var cashCode = await _core.GetMappedCashAccountAsync(order.PaymentMethod, order.Source, mapDict);
            lines.Add((receivablesAcct, order.TotalAmount, 0, _t.Get("Accounting.DebtRefundDesc", order.OrderNumber)));
            lines.Add((cashCode, 0, order.TotalAmount, _t.Get("Accounting.OrderAmountRefundDesc", order.OrderNumber)));
        }

        await _core.PostEntryAsync(JournalEntryType.PaymentVoucher, reference, _t.Get("Accounting.AutoPaymentVoucherMainDesc", order.OrderNumber), TimeHelper.GetEgyptBusinessDayDate(order.CreatedAt), lines, orderId: order.Id, customerId: order.CustomerId, source: order.Source, createdAt: order.CreatedAt);
    }

    public async Task PostReceiptVoucherAsync(ReceiptVoucher voucher, int? orderId = null)
    {
        if (await _core.EntryExistsAsync(JournalEntryType.ReceiptVoucher, voucher.VoucherNumber)) return;
        var lines = new List<(string code, decimal debit, decimal credit, string desc)> {
            ($"ID:{voucher.CashAccountId}", voucher.Amount, 0, _t.Get("Accounting.ReceiptVoucherShortDesc", voucher.VoucherNumber)),
            ($"ID:{voucher.FromAccountId}", 0, voucher.Amount, _t.Get("Accounting.FromAccountDesc", voucher.FromAccount?.NameAr ?? ""))
        };
        var entry = await _core.PostEntryAsync(JournalEntryType.ReceiptVoucher, voucher.VoucherNumber, voucher.Description ?? "", voucher.VoucherDate, lines, customerId: voucher.CustomerId, orderId: orderId, source: voucher.CostCenter, employeeId: voucher.EmployeeId);
        
        voucher.JournalEntryId = entry.Id;
        
        // ⚡ Outbox Pattern: Save event in the same transaction
        _dashboardEvents.NotifyTransactionOccurred(voucher.VoucherDate);
        await _db.SaveChangesAsync();
        _dashboardEvents.TriggerImmediateProcessing();
    }

    public async Task PostPaymentVoucherAsync(PaymentVoucher voucher)
    {
        if (await _core.EntryExistsAsync(JournalEntryType.PaymentVoucher, voucher.VoucherNumber)) return;
        var lines = new List<(string code, decimal debit, decimal credit, string desc)> {
            ($"ID:{voucher.ToAccountId}", voucher.Amount, 0, _t.Get("Accounting.PaymentVoucherShortDesc", voucher.VoucherNumber)),
            ($"ID:{voucher.CashAccountId}", 0, voucher.Amount, _t.Get("Accounting.FromCashAccountDesc", voucher.CashAccount?.NameAr ?? ""))
        };
        var entry = await _core.PostEntryAsync(JournalEntryType.PaymentVoucher, voucher.VoucherNumber, voucher.Description ?? "", voucher.VoucherDate, lines, supplierId: voucher.SupplierId, purchaseInvoiceId: voucher.PurchaseInvoiceId, source: voucher.CostCenter, employeeId: voucher.EmployeeId);
        
        voucher.JournalEntryId = entry.Id;
        
        // ⚡ Outbox Pattern: Save event in the same transaction
        _dashboardEvents.NotifyTransactionOccurred(voucher.VoucherDate);
        await _db.SaveChangesAsync();
        _dashboardEvents.TriggerImmediateProcessing();
    }

    public async Task PostSupplierPaymentAsync(SupplierPayment payment)
    {
        if (await _core.EntryExistsAsync(JournalEntryType.PaymentVoucher, payment.PaymentNumber)) return;
        var mapDict = await _core.GetSafeSystemMappingsAsync();
        string payablesAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Supplier, mapDict)}";
        
        string cashCode = payment.CashAccountId.HasValue 
            ? $"ID:{payment.CashAccountId.Value}" 
            : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.PaymentVoucherCash, mapDict)}";

        var lines = new List<(string code, decimal debit, decimal credit, string desc)> {
            (payablesAcct, payment.Amount, 0, _t.Get("Accounting.SupplierSettlementDesc", payment.PaymentNumber)),
            (cashCode, 0, payment.Amount, _t.Get("Accounting.FromCashAccountDesc", payment.AccountName ?? ""))
        };
        await _core.PostEntryAsync(JournalEntryType.PaymentVoucher, payment.PaymentNumber, _t.Get("Accounting.SupplierPaymentVoucherMainDesc", payment.PaymentNumber), payment.PaymentDate, lines, supplierId: payment.SupplierId, purchaseInvoiceId: payment.PurchaseInvoiceId, source: payment.CostCenter);
        
        try { Hangfire.BackgroundJob.Enqueue<IAccountingService>(a => a.SyncEntityBalancesAsync()); } catch { /* non-critical */ }
        
        // ⚡ Decoupled Event
        _dashboardEvents.NotifyTransactionOccurred(payment.PaymentDate);
    }
}
