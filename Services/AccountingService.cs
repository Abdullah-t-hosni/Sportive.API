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
    private const string DELIVERY_GROUP   = "4103";   // حساب التوصيل (رئيسي)
    private const string DELIVERY_REVENUE = "410301"; // إيراد خدمات توصيل
    private const string COGS            = "51101";  // تكلفة البضاعة المباعة
    private const string PURCHASES_NET   = "511";    // صافي المشتريات
    private const string PURCHASE_DISC   = "51103";  // خصم مكتسب (المشتريات)
    private const string VAT_OUTPUT      = "2104";   // ضريبة قيمة مضافة - دائنة (مبيعات)
    private const string VAT_INPUT       = "2105";   // ضريبة قيمة مضافة - مدينة (مشتريات)
    private const decimal VAT_RATE       = 0.14m;    // نسبة الضريبة (افتراضية 14%)

    public AccountingService(AppDbContext db) => _db = db;

    // ══════════════════════════════════════════════════════
    // 1. فاتورة مبيعات
    // ══════════════════════════════════════════════════════
    public async Task PostSalesOrderAsync(Order order)
    {
        if (await EntryExists(JournalEntryType.SalesInvoice, order.OrderNumber)) return;

        // جلب إعدادات المتجر للحصول على الحسابات المختارة ونسبة الضريبة
        var store = await _db.StoreInfo.FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        var vatRate = (store?.VatRatePercent ?? 14) / 100m;
        var deliveryRevAcct = !string.IsNullOrEmpty(store?.DeliveryRevenueAccountId) ? store.DeliveryRevenueAccountId 
                             : (!string.IsNullOrEmpty(store?.DeliveryAccountId) ? store.DeliveryAccountId : DELIVERY_REVENUE);
        var vatAcct = !string.IsNullOrEmpty(store?.StoreVatAccountId) ? store.StoreVatAccountId : VAT_OUTPUT;

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        // ── 1. حسابات الجانب الدائن (Credits) ──────────────────
        decimal totalVatAmount = 0;
        decimal totalNetRevenue = 0;

        if (order.Items != null && order.Items.Any())
        {
            foreach (var item in order.Items)
            {
                decimal itemVat = 0;
                decimal itemNet = item.TotalPrice;

                if (item.Product != null && item.Product.HasTax)
                {
                    itemNet = Math.Round(item.TotalPrice / (1 + vatRate), 2);
                    itemVat = item.TotalPrice - itemNet;
                }
                
                totalNetRevenue += itemNet;
                totalVatAmount += itemVat;
            }
        }
        else
        {
            totalNetRevenue = Math.Round(order.SubTotal / (1 + vatRate), 2);
            totalVatAmount = order.SubTotal - totalNetRevenue;
        }

        lines.Add((SALES_REVENUE, 0, totalNetRevenue, $"مبيعات - {order.OrderNumber} (الإيراد)"));
        
        if (totalVatAmount > 0)
            lines.Add((vatAcct, 0, totalVatAmount, $"ضريبة مبيعات {(store?.VatRatePercent ?? 14)}% - {order.OrderNumber} (دائن)"));

        if (order.DeliveryFee > 0)
            lines.Add((deliveryRevAcct, 0, order.DeliveryFee, $"إيراد توصيل - {order.OrderNumber} (دائن)"));

        // ── 2. حسابات الجانب المدين (Debits) ──────────────────
        if (order.DiscountAmount > 0)
            lines.Add((SALES_DISCOUNT, order.DiscountAmount, 0, $"خصم ممنوح لعميل - {order.OrderNumber} (مدين)"));

        var netReceivable = order.TotalAmount; 
        
        bool isCashCollection = (order.Source == OrderSource.POS && order.PaymentStatus == PaymentStatus.Paid) ||
                                (order.Source == OrderSource.Website && order.PaymentMethod == PaymentMethod.Cash && order.PaymentStatus == PaymentStatus.Paid);

        if (isCashCollection)
        {
            var cashCode = GetCashAccount(order.PaymentMethod, order.Source);
            var sourceName = order.Source == OrderSource.POS ? "كاشير" : "موقع";
            lines.Add((cashCode, netReceivable, 0, $"تحصيل نقدي {sourceName} - {order.OrderNumber} (مدين)"));
        }
        else
        {
            lines.Add((RECEIVABLES, netReceivable, 0, $"مستحق على العميل - {order.OrderNumber} (مدين)"));
        }

        // ── 3. قيد التكلفة (نظام الجرد المستمر) ───────────────
        var totalCost = order.Items?.Sum(i => (i.Product?.CostPrice ?? 0) * i.Quantity) ?? 0;
        if (totalCost > 0)
        {
            lines.Add((COGS, totalCost, 0, $"تكلفة المبيعات - {order.OrderNumber} (مدين)"));
            lines.Add((INVENTORY, 0, totalCost, $"خروج مخزون - {order.OrderNumber} (دائن)"));
        }

        await PostEntry(
            type:        JournalEntryType.SalesInvoice,
            reference:   order.OrderNumber,
            description: $"فاتورة مبيعات {order.OrderNumber} - {order.Customer?.FullName}",
            date:        order.CreatedAt,
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId,
            source:      order.Source
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

        // ── 1. الجانب المدين: المخزون + الضريبة ──────────────────────────────
        var invAcct = invoice.InventoryAccountId?.ToString() ?? INVENTORY;
        lines.Add((invAcct, invoice.SubTotal, 0, $"مشتريات بضاعة - {invoice.InvoiceNumber}"));

        if (invoice.TaxAmount > 0)
        {
            var vatAcct = invoice.VatAccountId?.ToString() ?? VAT_INPUT;
            lines.Add((vatAcct, invoice.TaxAmount, 0, $"ضريبة مشتريات مدخلات - {invoice.InvoiceNumber}"));
        }

        // ── 2. الجانب الدائن: المورد + الخصم المكتسب ──────────────────────────
        var vendorAcct = invoice.VendorAccountId?.ToString() 
                        ?? invoice.Supplier?.MainAccountId?.ToString() 
                        ?? PAYABLES;
        lines.Add((vendorAcct, 0, invoice.TotalAmount, $"إثبات استحقاق للمورد (صافي) - {invoice.InvoiceNumber}"));

        // ── 3. السداد النقدي (إن وجد) ──────────────────────────────────────────
        if (invoice.PaymentTerms == PaymentTerms.Cash)
        {
            var cashAcct = invoice.CashAccountId?.ToString() ?? CASH_ACCOUNTS;
            lines.Add((vendorAcct, invoice.TotalAmount, 0, $"سداد نقدي فوري - {invoice.InvoiceNumber}"));
            lines.Add((cashAcct, 0, invoice.TotalAmount, $"صرف من نقدية الحسابات - {invoice.InvoiceNumber}"));
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

        var vendorAcct = invoice.VendorAccountId?.ToString() 
                        ?? invoice.Supplier?.MainAccountId?.ToString() 
                        ?? PAYABLES;
        var expAcct    = invoice.ExpenseAccountId?.ToString() ?? PURCHASES_NET;
        var vatAcct    = invoice.VatAccountId?.ToString() ?? VAT_INPUT;

        lines.Add((vendorAcct, invoice.TotalAmount, 0,                $"رد للمورد - {invoice.InvoiceNumber}"));
        lines.Add((expAcct,    0,                  invoice.SubTotal,  $"مرتجع مشتريات - {invoice.InvoiceNumber}"));

        if (invoice.TaxAmount > 0)
            lines.Add((vatAcct, 0, invoice.TaxAmount, $"استرداد ضريبة - {invoice.InvoiceNumber}"));

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
            (PaymentMethod.Cash,       OrderSource.Website) => CASH_WEBSITE,
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

    /// يجيب Id الحساب من الكود أو Id
    /// المنطق: إذا كان الإدخال رقماً ويشبه Id (قصير ≤ 6 أرقام ولا يبدأ بصفر)
    /// نحاول الكود أولاً لأن أكواد الحسابات (مثل 2104, 1106) قد تتطابق مع Id صغيرة
    private async Task<int> GetAccountIdAsync(string input)
    {
        // أولاً: دائماً نحاول البحث بالكود (Code) لأن الأكواد المحاسبية أهم
        var acctByCode = await _db.Accounts
            .Where(a => a.Code == input && !a.IsDeleted && a.IsActive)
            .Select(a => new { a.Id })
            .FirstOrDefaultAsync();

        if (acctByCode != null)
            return acctByCode.Id;

        // ثانياً: إذا لم يوجد بالكود، نحاول بالـ Id (للحالات التي يُمرَّر فيها Id مباشرة)
        if (int.TryParse(input, out var id) && id > 0)
        {
            var acctById = await _db.Accounts
                .Where(a => a.Id == id && !a.IsDeleted && a.IsActive)
                .Select(a => new { a.Id })
                .FirstOrDefaultAsync();

            if (acctById != null)
                return acctById.Id;
        }

        throw new InvalidOperationException($"حساب '{input}' غير موجود أو غير نشط في شجرة الحسابات");
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
        int? supplierId = null,
        OrderSource? source = null)
    {
        // التحقق من التوازن
        var totalDr = lines.Sum(l => l.debit);
        var totalCr = lines.Sum(l => l.credit);
        if (Math.Round(totalDr, 2) != Math.Round(totalCr, 2))
            throw new InvalidOperationException(
                $"القيد غير متوازن: مدين={totalDr}, دائن={totalCr} | {reference}");

        var count   = await _db.JournalEntries.IgnoreQueryFilters().CountAsync() + 1;
        var year    = date.Year % 100;
        
        // تمييز أرقام القيود بناءً على المصدر
        string entryNo;
        if (source == OrderSource.POS)
            entryNo = $"JE-POS-{year}{count:D5}";
        else if (source == OrderSource.Website)
            entryNo = $"JE-WEB-{year}{count:D5}";
        else
            entryNo = $"JE-{year}{count:D5}";

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
