using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Services;

/// <summary>
/// يولّد قيود محاسبية تلقائية لكل عملية تجارية
/// القيود مبنية على شجرة الحسابات الخاصة بك من ملف Accounts.xlsx
/// </summary>
public interface IAccountingService
{
    Task PostSalesOrderAsync(Order order);
    Task PostSalesReturnAsync(Order order);
    Task PostPurchaseInvoiceAsync(PurchaseInvoice invoice);
    Task PostPurchaseReturnAsync(PurchaseInvoice invoice);
    Task PostSupplierPaymentAsync(SupplierPayment payment);
    Task ReverseEntryAsync(int journalEntryId, string reason);
}

public class AccountingService : IAccountingService
{
    private readonly AppDbContext _db;

    // ── كودات الحسابات الثابتة من شجرة حساباتك ──────────
    // النقدية والصناديق
    private const string CASH_CASHIER    = "110101"; // نقدية الكاشير
    private const string CASH_WEBSITE    = "110102"; // نقدية الموقع (كاش عند الاستلام)
    private const string CASH_ACCOUNTS   = "110103"; // نقدية الحسابات
    private const string BANK            = "110201"; // حساب البنك
    private const string VODAFONE        = "110701"; // محفظة فودافون كاش
    private const string INSTAPAY        = "110703"; // انستاباي
    private const string VODAFONE_WEB    = "110702"; // فودافون - الموقع
    private const string INSTAPAY_WEB    = "110704"; // انستاباي - الموقع
    // العملاء والموردين
    private const string RECEIVABLES     = "1103";   // العملاء
    private const string PAYABLES        = "2101";   // الموردين
    // المخزون والإيرادات
    private const string INVENTORY       = "1106";   // المخزون
    private const string SALES_REVENUE   = "4101";   // إيرادات المبيعات
    private const string SALES_RETURN    = "4102";   // مرتجع المبيعات
    private const string SALES_DISCOUNT  = "410101"; // الخصم الممنوح
    private const string DELIVERY_REVENUE = "410105"; // إيراد خدمات توصيل (تحت 4101؛ لا 410102 بسبب تقاطع مع 4102)
    private const string COGS            = "51101";  // تكلفة البضاعة المباعة
    private const string PURCHASES_NET   = "511";    // صافي المشتريات
    private const string PURCHASE_DISC   = "51103";  // خصم مكتسب (المشتريات)

    public AccountingService(AppDbContext db) => _db = db;

    // ══════════════════════════════════════════════════════
    // 1. فاتورة مبيعات
    // ══════════════════════════════════════════════════════
    public async Task PostSalesOrderAsync(Order order)
    {
        // لا تُعيد الترحيل لو القيد موجود
        if (await EntryExists(JournalEntryType.SalesInvoice, order.OrderNumber)) return;

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        // ── الجانب الدائن: الإيرادات ──────────────────────
        lines.Add((SALES_REVENUE, 0, order.SubTotal, $"مبيعات - {order.OrderNumber}"));
        
        // إيراد التوصيل (هام لموازنة القيد)
        if (order.DeliveryFee > 0)
            lines.Add((DELIVERY_REVENUE, 0, order.DeliveryFee, $"إيراد توصيل - {order.OrderNumber}"));

        // ── خصم ممنوح إذا وجد (مدين) ──────────────────────
        if (order.DiscountAmount > 0)
            lines.Add((SALES_DISCOUNT, order.DiscountAmount, 0, $"خصم لعميل - {order.OrderNumber}"));

        // ── الجانب المدين: النقدية أو المدينون ─────────────
        var cashCode = GetCashAccount(order.PaymentMethod, order.Source);
        var netReceivable = order.TotalAmount; // المبلغ النهائي (SubTotal + Delivery - Discount)

        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            // دفع فوري → مدين النقدية
            lines.Add((cashCode, netReceivable, 0, $"تحصيل نقدي - {order.OrderNumber}"));
        }
        else
        {
            // آجل → مدين حساب العملاء
            lines.Add((RECEIVABLES, netReceivable, 0, $"مستحق على العميل - {order.OrderNumber}"));
        }

        // ── نظام الجرد المستمر: التكلفة والمخزون ─────────
        var totalCost = order.Items?.Sum(i => (i.Product?.CostPrice ?? 0) * i.Quantity) ?? 0;
        if (totalCost > 0)
        {
            lines.Add((COGS,      totalCost, 0,         $"تكلفة المبيعات - {order.OrderNumber}"));
            lines.Add((INVENTORY, 0,         totalCost, $"خروج مخزون - {order.OrderNumber}"));
        }

        await PostEntry(
            type:        JournalEntryType.SalesInvoice,
            reference:   order.OrderNumber,
            description: $"فاتورة مبيعات {order.OrderNumber} - {order.Customer?.FullName}",
            date:        order.CreatedAt,
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId
        );
    }

    // ══════════════════════════════════════════════════════
    // 2. مرتجع مبيعات
    // ══════════════════════════════════════════════════════
    public async Task PostSalesReturnAsync(Order order)
    {
        if (await EntryExists(JournalEntryType.SalesReturn, order.OrderNumber + "-RTN")) return;

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        // عكس القيد المالي
        lines.Add((SALES_RETURN,  order.TotalAmount, 0,               $"مرتجع مبيعات - {order.OrderNumber}"));
        lines.Add((RECEIVABLES,   0,                 order.TotalAmount, $"دائن العميل - {order.OrderNumber}"));

        // عكس قيد التكلفة (إعادة للمخزون)
        var totalCost = order.Items?.Sum(i => (i.Product?.CostPrice ?? 0) * i.Quantity) ?? 0;
        if (totalCost > 0)
        {
            lines.Add((INVENTORY, totalCost, 0,         $"إعادة للمخزون - {order.OrderNumber}"));
            lines.Add((COGS,      0,         totalCost, $"تخفيض تكلفة المبيعات - {order.OrderNumber}"));
        }

        await PostEntry(
            type:        JournalEntryType.SalesReturn,
            reference:   order.OrderNumber + "-RTN",
            description: $"مرتجع مبيعات {order.OrderNumber}",
            date:        DateTime.UtcNow,
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId
        );
    }

    // ══════════════════════════════════════════════════════
    // 3. فاتورة مشتريات
    // ══════════════════════════════════════════════════════
    public async Task PostPurchaseInvoiceAsync(PurchaseInvoice invoice)
    {
        if (await EntryExists(JournalEntryType.PurchaseInvoice, invoice.InvoiceNumber)) return;

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        // مدين المخزون
        lines.Add((INVENTORY, invoice.SubTotal, 0,
            $"مشتريات بضاعة - {invoice.InvoiceNumber}"));

        // ضريبة إذا وجدت (مدين ضريبة القيمة المضافة)
        if (invoice.TaxAmount > 0)
            lines.Add(("2105", invoice.TaxAmount, 0,
                $"ضريبة مشتريات - {invoice.InvoiceNumber}"));

        // دائن — نقدي أم آجل؟
        if (invoice.PaymentTerms == PaymentTerms.Cash)
        {
            lines.Add((CASH_ACCOUNTS, 0, invoice.TotalAmount,
                $"دفع نقدي - {invoice.InvoiceNumber}"));
        }
        else
        {
            // آجل → دائن حساب الموردين
            lines.Add((PAYABLES, 0, invoice.TotalAmount,
                $"مستحق للمورد - {invoice.InvoiceNumber}"));
        }

        await PostEntry(
            type:        JournalEntryType.PurchaseInvoice,
            reference:   invoice.InvoiceNumber,
            description: $"فاتورة مشتريات {invoice.InvoiceNumber} - {invoice.Supplier?.Name}",
            date:        invoice.InvoiceDate,
            lines:       lines,
            supplierId:  invoice.SupplierId
        );
    }

    // ══════════════════════════════════════════════════════
    // 4. مرتجع مشتريات
    // ══════════════════════════════════════════════════════
    public async Task PostPurchaseReturnAsync(PurchaseInvoice invoice)
    {
        var refNo = invoice.InvoiceNumber + "-RTN";
        if (await EntryExists(JournalEntryType.PurchaseReturn, refNo)) return;

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        lines.Add((PAYABLES,      invoice.TotalAmount, 0,                $"رد للمورد - {invoice.InvoiceNumber}"));
        lines.Add((PURCHASES_NET, 0,                  invoice.SubTotal,  $"مرتجع مشتريات - {invoice.InvoiceNumber}"));

        if (invoice.TaxAmount > 0)
            lines.Add(("2105", 0, invoice.TaxAmount, $"استرداد ضريبة - {invoice.InvoiceNumber}"));

        await PostEntry(
            type:        JournalEntryType.PurchaseReturn,
            reference:   refNo,
            description: $"مرتجع مشتريات {invoice.InvoiceNumber}",
            date:        DateTime.UtcNow,
            lines:       lines,
            supplierId:  invoice.SupplierId
        );
    }

    // ══════════════════════════════════════════════════════
    // 5. سند صرف مورد
    // ══════════════════════════════════════════════════════
    public async Task PostSupplierPaymentAsync(SupplierPayment payment)
    {
        if (await EntryExists(JournalEntryType.PaymentVoucher, payment.PaymentNumber)) return;

        var cashCode = GetCashFromAccountName(payment.AccountName);

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>
        {
            (PAYABLES,  payment.Amount, 0,              $"تسوية مورد - {payment.PaymentNumber}"),
            (cashCode,  0,              payment.Amount, $"صرف نقدي - {payment.PaymentNumber}"),
        };

        await PostEntry(
            type:        JournalEntryType.PaymentVoucher,
            reference:   payment.PaymentNumber,
            description: $"سند صرف مورد {payment.PaymentNumber} - {payment.Supplier?.Name}",
            date:        payment.PaymentDate,
            lines:       lines,
            supplierId:  payment.SupplierId
        );
    }

    // ══════════════════════════════════════════════════════
    // 6. عكس قيد
    // ══════════════════════════════════════════════════════
    public async Task ReverseEntryAsync(int journalEntryId, string reason)
    {
        var entry = await _db.JournalEntries
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == journalEntryId && !e.IsDeleted);

        if (entry == null || entry.Status == JournalEntryStatus.Reversed) return;

        var count  = await _db.JournalEntries.IgnoreQueryFilters().CountAsync() + 1;
        var year   = DateTime.UtcNow.Year % 100;
        var revNo  = $"JE-{year}{count:D5}";

        var reversal = new JournalEntry
        {
            EntryNumber  = revNo,
            EntryDate    = DateTime.UtcNow,
            Type         = entry.Type,
            Status       = JournalEntryStatus.Posted,
            Reference    = entry.EntryNumber,
            Description  = $"عكس: {entry.EntryNumber} — {reason}",
            ReversalOfId = entry.Id,
            CreatedAt    = DateTime.UtcNow,
        };

        foreach (var line in entry.Lines)
        {
            reversal.Lines.Add(new JournalLine
            {
                AccountId   = line.AccountId,
                Debit       = line.Credit,  // swap
                Credit      = line.Debit,
                Description = line.Description,
                CustomerId  = line.CustomerId,
                SupplierId  = line.SupplierId,
                CreatedAt   = DateTime.UtcNow,
            });
        }

        entry.Status    = JournalEntryStatus.Reversed;
        entry.UpdatedAt = DateTime.UtcNow;

        _db.JournalEntries.Add(reversal);
        await _db.SaveChangesAsync();
    }

    // ══════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ══════════════════════════════════════════════════════

    /// يختار حساب النقدية بناءً على طريقة الدفع والمصدر
    private static string GetCashAccount(PaymentMethod method, OrderSource source)
    {
        return (method, source) switch
        {
            (PaymentMethod.Vodafone,   OrderSource.POS)     => VODAFONE,
            (PaymentMethod.Vodafone,   OrderSource.Website) => VODAFONE_WEB,
            (PaymentMethod.InstaPay,   OrderSource.POS)     => INSTAPAY,
            (PaymentMethod.InstaPay,   OrderSource.Website) => INSTAPAY_WEB,
            (PaymentMethod.CreditCard, _)                   => CASH_ACCOUNTS,
            (_,                        OrderSource.POS)     => CASH_CASHIER,
            _                                               => CASH_WEBSITE,
        };
    }

    /// يختار حساب النقدية من اسم الحساب النصي (من سندات الصرف)
    private static string GetCashFromAccountName(string accountName) =>
        accountName switch
        {
            var n when n.Contains("كاشير")                  => CASH_CASHIER,
            var n when n.Contains("بنك") || n.Contains("Bank") => BANK,
            var n when n.Contains("فودافون")                => VODAFONE,
            var n when n.Contains("انستاباي") || n.Contains("InstaPay") => INSTAPAY,
            _                                               => CASH_ACCOUNTS,
        };

    /// يجيب Id الحساب من الكود
    private async Task<int> GetAccountIdAsync(string code)
    {
        var acct = await _db.Accounts
            .Where(a => a.Code == code && !a.IsDeleted && a.IsActive)
            .Select(a => new { a.Id, a.AllowPosting })
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException($"حساب '{code}' غير موجود أو غير نشط");

        if (!acct.AllowPosting)
            throw new InvalidOperationException($"حساب '{code}' لا يقبل الترحيل المباشر");

        return acct.Id;
    }

    /// يتحقق إذا كان القيد موجود مسبقاً (يمنع التكرار)
    private async Task<bool> EntryExists(JournalEntryType type, string reference) =>
        await _db.JournalEntries
            .AnyAsync(e => !e.IsDeleted && e.Type == type && e.Reference == reference);

    /// ينشئ ويرحّل القيد
    private async Task PostEntry(
        JournalEntryType type,
        string reference,
        string description,
        DateTime date,
        List<(string code, decimal debit, decimal credit, string desc)> lines,
        int? orderId    = null,
        int? customerId = null,
        int? supplierId = null)
    {
        // التحقق من التوازن
        var totalDr = lines.Sum(l => l.debit);
        var totalCr = lines.Sum(l => l.credit);
        if (Math.Round(totalDr, 2) != Math.Round(totalCr, 2))
            throw new InvalidOperationException(
                $"القيد غير متوازن: مدين={totalDr}, دائن={totalCr} | {reference}");

        var count   = await _db.JournalEntries.IgnoreQueryFilters().CountAsync() + 1;
        var year    = date.Year % 100;
        var entryNo = $"JE-{year}{count:D5}";

        var entry = new JournalEntry
        {
            EntryNumber = entryNo,
            EntryDate   = date,
            Type        = type,
            Status      = JournalEntryStatus.Posted,
            Reference   = reference,
            Description = description,
            CreatedAt   = DateTime.UtcNow,
        };

        foreach (var (code, debit, credit, desc) in lines)
        {
            if (debit == 0 && credit == 0) continue;
            var accountId = await GetAccountIdAsync(code);
            entry.Lines.Add(new JournalLine
            {
                AccountId   = accountId,
                Debit       = debit,
                Credit      = credit,
                Description = desc,
                CustomerId  = customerId,
                SupplierId  = supplierId,
                OrderId     = orderId,
                CreatedAt   = DateTime.UtcNow,
            });
        }

        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync();
    }
}
