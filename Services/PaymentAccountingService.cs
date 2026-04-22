using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
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

    public PaymentAccountingService(AppDbContext db, AccountingCoreService core, ILogger<PaymentAccountingService> logger)
    {
        _db = db;
        _core = core;
        _logger = logger;
    }

    public async Task PostOrderPaymentAsync(Order order)
    {
        var reference = order.OrderNumber + "-PMT";
        
        // 🚨 AUTO-UPDATE: حذف القيد القديم إن وجد للسماح بالتعديل
        var existing = await _db.JournalEntries
            .FirstOrDefaultAsync(e => e.Type == JournalEntryType.ReceiptVoucher && e.Reference == reference);
        
        if (existing != null)
        {
            _db.JournalEntries.Remove(existing);
            await _db.SaveChangesAsync();
        }

        // ✅ NEW: Prevent double-accounting for POS orders where payments are already merged into the SalesInvoice
        var invoiceEntry = await _db.JournalEntries
            .Include(e => e.Lines)
                .ThenInclude(l => l.Account)
            .FirstOrDefaultAsync(e => e.Type == JournalEntryType.SalesInvoice && e.Reference == order.OrderNumber);
            
        if (invoiceEntry != null && invoiceEntry.Lines.Any(l => l.Debit > 0 && l.Account != null && 
            (l.Account.Code.StartsWith("1101") || l.Account.Code.StartsWith("1102") || l.Account.Code.StartsWith("1105"))))
        {
            _logger.LogInformation("[Accounting] Skipping separate PaymentVoucher for order {OrderNum}; already merged in SalesInvoice.", order.OrderNumber);
            return;
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
                var code = await _core.GetMappedCashAccountAsync(p.Method, order.Source, mapDict);
                lines.Add((code, p.Amount, 0, $"تحصيل ({_core.GetMethodLabel(p.Method)}) - {order.OrderNumber}"));
                lines.Add((receivablesAcct, 0, p.Amount, $"إغلاق مديونية ({_core.GetMethodLabel(p.Method)}) - {order.OrderNumber}"));
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
                    var code = await _core.GetMappedCashAccountAsync(m, order.Source, mapDict);
                    lines.Add((code, v, 0, $"تحصيل ({_core.GetMethodLabel(m)}) - {order.OrderNumber}"));
                    lines.Add((receivablesAcct, 0, v, $"إغلاق مديونية ({_core.GetMethodLabel(m)}) - {order.OrderNumber}"));
                    handledPaidAmt += v;
                }
            }
            else if (order.PaymentMethod != PaymentMethod.Credit && order.TotalAmount > 0)
            {
                var cashCode = await _core.GetMappedCashAccountAsync(order.PaymentMethod, order.Source, mapDict);
                lines.Add((cashCode, order.TotalAmount, 0, $"تحصيل طلب {order.OrderNumber} ({_core.GetMethodLabel(order.PaymentMethod)})"));
                lines.Add((receivablesAcct, 0, order.TotalAmount, $"إغلاق مديونية طلب {order.OrderNumber}"));
                handledPaidAmt = order.TotalAmount;
            }
        }

        // ⚠️ STRICT VALIDATION: Ensure ledger entries match order records
        if (!lines.Any()) return;
        
        if (order.PaidAmount > 0 && Math.Abs(handledPaidAmt - order.PaidAmount) > 0.01m)
        {
            throw new InvalidOperationException($"خطأ في مطابقة تحصيل الطلب: المبلغ المسجل في سجلات الطلب ({order.PaidAmount}) لا يطابق واقع القيود ({handledPaidAmt}) للطلب {order.OrderNumber}");
        }

        await _core.PostEntryAsync(JournalEntryType.ReceiptVoucher, reference, $"تحصيل تلقائي للطلب {order.OrderNumber}", TimeHelper.GetEgyptTime(), lines, orderId: order.Id, customerId: order.CustomerId, source: order.Source);
    }

    public async Task PostOrderRefundAsync(Order order)
    {
        var reference = order.OrderNumber + "-RFD";
        if (await _core.EntryExistsAsync(JournalEntryType.PaymentVoucher, reference)) return;

        var mapDict = await _core.GetSafeSystemMappingsAsync();
        string receivablesAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Customer, mapDict)}";
        var cashCode = await _core.GetMappedCashAccountAsync(order.PaymentMethod, order.Source, mapDict);

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();
        lines.Add((receivablesAcct, order.TotalAmount, 0, $"رد مديونية لطلب {order.OrderNumber}"));
        lines.Add((cashCode, 0, order.TotalAmount, $"رد مبلغ الطلب {order.OrderNumber}"));

        await _core.PostEntryAsync(JournalEntryType.PaymentVoucher, reference, $"رد تلقائي للطلب {order.OrderNumber}", TimeHelper.GetEgyptTime(), lines, orderId: order.Id, customerId: order.CustomerId, source: order.Source);
    }

    public async Task PostReceiptVoucherAsync(ReceiptVoucher voucher, int? orderId = null)
    {
        var lines = new List<(string code, decimal debit, decimal credit, string desc)> {
            ($"ID:{voucher.CashAccountId}", voucher.Amount, 0, $"سند قبض {voucher.VoucherNumber}"),
            ($"ID:{voucher.FromAccountId}", 0, voucher.Amount, $"من حساب {voucher.FromAccount?.NameAr}")
        };
        var entry = await _core.PostEntryAsync(JournalEntryType.ReceiptVoucher, voucher.VoucherNumber, voucher.Description ?? "", voucher.VoucherDate, lines, customerId: voucher.CustomerId, orderId: orderId, source: voucher.CostCenter, employeeId: voucher.EmployeeId);
        
        // Update voucher with Entry ID to prevent broken links
        voucher.JournalEntryId = entry.Id;
        await _db.SaveChangesAsync();
    }

    public async Task PostPaymentVoucherAsync(PaymentVoucher voucher)
    {
        var lines = new List<(string code, decimal debit, decimal credit, string desc)> {
            ($"ID:{voucher.ToAccountId}", voucher.Amount, 0, $"سند دفع {voucher.VoucherNumber}"),
            ($"ID:{voucher.CashAccountId}", 0, voucher.Amount, $"صرف من {voucher.CashAccount?.NameAr}")
        };
        var entry = await _core.PostEntryAsync(JournalEntryType.PaymentVoucher, voucher.VoucherNumber, voucher.Description ?? "", voucher.VoucherDate, lines, supplierId: voucher.SupplierId, purchaseInvoiceId: voucher.PurchaseInvoiceId, source: voucher.CostCenter, employeeId: voucher.EmployeeId);
        
        voucher.JournalEntryId = entry.Id;
        await _db.SaveChangesAsync();
    }

    public async Task PostSupplierPaymentAsync(SupplierPayment payment)
    {
        // 🚨 AUTO-UPDATE: حذف القيد القديم إن وجد للسماح بالتعديل
        var existing = await _db.JournalEntries
            .FirstOrDefaultAsync(e => e.Type == JournalEntryType.PaymentVoucher && e.Reference == payment.PaymentNumber);
        if (existing != null)
        {
            _db.JournalEntries.Remove(existing);
            await _db.SaveChangesAsync();
        }

        var mapDict = await _core.GetSafeSystemMappingsAsync();
        string payablesAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Supplier, mapDict)}";
        
        // Prefer specific CashAccountId selected by user, fallback to system mappings
        string cashCode = payment.CashAccountId.HasValue 
            ? $"ID:{payment.CashAccountId.Value}" 
            : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.PaymentVoucherCash, mapDict)}";

        var lines = new List<(string code, decimal debit, decimal credit, string desc)> {
            (payablesAcct, payment.Amount, 0, $"تسوية مورد - {payment.PaymentNumber}"),
            (cashCode, 0, payment.Amount, $"صرف من {payment.AccountName}")
        };
        await _core.PostEntryAsync(JournalEntryType.PaymentVoucher, payment.PaymentNumber, $"سند صرف مورد {payment.PaymentNumber}", payment.PaymentDate, lines, supplierId: payment.SupplierId, purchaseInvoiceId: payment.PurchaseInvoiceId, source: payment.CostCenter);
        
        // Immediate status update
        await _core.SyncEntityBalancesAsync();
    }
}
