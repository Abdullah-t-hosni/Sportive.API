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
        if (await _core.EntryExistsAsync(JournalEntryType.ReceiptVoucher, reference)) return;

        var mapDict = await _core.GetSafeSystemMappingsAsync();
        string receivablesAcct = order.Customer?.MainAccountId != null ? $"ID:{order.Customer.MainAccountId}" : _core.GetMap(mapDict, MK.Customer, AccountingCoreService.RECEIVABLES);
        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        var splits = _core.ParseMixedPayments(order.AdminNotes);
        if (splits.Count > 0)
        {
            foreach (var (m, v) in splits)
            {
                var code = await _core.GetMappedCashAccountAsync(m, order.Source, mapDict);
                lines.Add((code, v, 0, $"تحصيل ({_core.GetMethodLabel(m)}) - {order.OrderNumber}"));
                lines.Add((receivablesAcct, 0, v, $"إغلاق مديونية ({_core.GetMethodLabel(m)}) - {order.OrderNumber}"));
            }
        }
        else
        {
            var cashCode = await _core.GetMappedCashAccountAsync(order.PaymentMethod, order.Source, mapDict);
            lines.Add((cashCode, order.TotalAmount, 0, $"تحصيل طلب {order.OrderNumber} ({_core.GetMethodLabel(order.PaymentMethod)})"));
            lines.Add((receivablesAcct, 0, order.TotalAmount, $"إغلاق مديونية طلب {order.OrderNumber}"));
        }

        await _core.PostEntryAsync(JournalEntryType.ReceiptVoucher, reference, $"تحصيل تلقائي للطلب {order.OrderNumber}", TimeHelper.GetEgyptTime(), lines, orderId: order.Id, customerId: order.CustomerId, source: order.Source);
    }

    public async Task PostOrderRefundAsync(Order order)
    {
        var reference = order.OrderNumber + "-RFD";
        if (await _core.EntryExistsAsync(JournalEntryType.PaymentVoucher, reference)) return;

        var mapDict = await _core.GetSafeSystemMappingsAsync();
        string receivablesAcct = _core.GetMap(mapDict, MK.Customer, AccountingCoreService.RECEIVABLES);
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
        await _core.PostEntryAsync(JournalEntryType.ReceiptVoucher, voucher.VoucherNumber, voucher.Description ?? "", voucher.VoucherDate, lines, customerId: voucher.CustomerId, orderId: orderId);
    }

    public async Task PostPaymentVoucherAsync(PaymentVoucher voucher)
    {
        var lines = new List<(string code, decimal debit, decimal credit, string desc)> {
            ($"ID:{voucher.ToAccountId}", voucher.Amount, 0, $"سند دفع {voucher.VoucherNumber}"),
            ($"ID:{voucher.CashAccountId}", 0, voucher.Amount, $"صرف من {voucher.CashAccount?.NameAr}")
        };
        await _core.PostEntryAsync(JournalEntryType.PaymentVoucher, voucher.VoucherNumber, voucher.Description ?? "", voucher.VoucherDate, lines, supplierId: voucher.SupplierId);
    }

    public async Task PostSupplierPaymentAsync(SupplierPayment payment)
    {
        if (await _core.EntryExistsAsync(JournalEntryType.PaymentVoucher, payment.PaymentNumber)) return;
        var mapDict = await _core.GetSafeSystemMappingsAsync();
        string payablesAcct = _core.GetMap(mapDict, MK.Supplier, AccountingCoreService.PAYABLES);
        var cashCode = payment.AccountName.Contains("كاشير") ? AccountingCoreService.CASH_CASHIER : AccountingCoreService.CASH_ACCOUNTS;

        var lines = new List<(string code, decimal debit, decimal credit, string desc)> {
            (payablesAcct, payment.Amount, 0, $"تسوية مورد - {payment.PaymentNumber}"),
            (cashCode, 0, payment.Amount, $"صرف من {payment.AccountName}")
        };
        await _core.PostEntryAsync(JournalEntryType.PaymentVoucher, payment.PaymentNumber, $"سند صرف مورد {payment.PaymentNumber}", payment.PaymentDate, lines, supplierId: payment.SupplierId);
    }
}
