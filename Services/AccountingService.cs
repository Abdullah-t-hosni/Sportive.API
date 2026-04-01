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
    Task PostOrderPaymentAsync(Order order);
    Task PostOrderRefundAsync(Order order);
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

        // Fetch Store info and Mappings
        var store = await _db.StoreInfo.FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        var vatRate = (store?.VatRatePercent ?? 14) / 100m;
        
        var mappings = await _db.AccountSystemMappings.Where(m => !m.IsDeleted).ToListAsync();
        // Force keys to lowercase for robust lookup
        var mapDict = mappings.ToDictionary(m => m.Key.ToLower(), m => m.AccountId);

        // --- Ledger Mapping ---
        string salesRevAcct   = GetMap(mapDict, "salesAccountID", SALES_REVENUE);
        string salesDiscAcct  = GetMap(mapDict, "salesDiscountAccountID", SALES_DISCOUNT);
        string inventoryAcct  = GetMap(mapDict, "inventoryAccountID", INVENTORY);
        string cogsAcct       = GetMap(mapDict, "costOfGoodsSoldAccountID", COGS);
        string receivablesAcct = GetMap(mapDict, "customerAccountID", RECEIVABLES);
        
        // Delivery Revenue (Store specific > Web Mapping > Default)
        string deliveryRevAcct = !string.IsNullOrEmpty(store?.DeliveryRevenueAccountId) ? store.DeliveryRevenueAccountId 
                             : GetMap(mapDict, "webDeliveryRevenueAccountID", DELIVERY_REVENUE);

        // VAT (Store specific > Output Mapping > Default)
        string vatAcct = !string.IsNullOrEmpty(store?.StoreVatAccountId) ? store.StoreVatAccountId 
                       : GetMap(mapDict, "vatOutputAccountID", VAT_OUTPUT);

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        // ── 1. Credits (Revenue + VAT + Delivery) ─────────────
        decimal totalVatAmount = 0;
        decimal totalNetRevenue = 0;

        if (order.Items != null && order.Items.Any())
        {
            foreach (var item in order.Items)
            {
                decimal itemVat = 0;
                decimal itemNet = item.TotalPrice;

                if (item.HasTax)
                {
                    // Use the rate captured at the time of order
                    var rate = (item.VatRateApplied ?? 14) / 100m;
                    itemNet = Math.Round(item.TotalPrice / (1 + rate), 2);
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

        lines.Add((salesRevAcct, 0, totalNetRevenue, $"مبيعات - {order.OrderNumber} (الإيراد)"));
        
        if (totalVatAmount > 0)
            lines.Add((vatAcct, 0, totalVatAmount, $"ضريبة مبيعات {(store?.VatRatePercent ?? 14)}% - {order.OrderNumber} (دائن)"));

        if (order.DeliveryFee > 0)
            lines.Add((deliveryRevAcct, 0, order.DeliveryFee, $"إيراد توصيل - {order.OrderNumber} (دائن)"));

        // ── 2. Debits (Discount + Receivables/Cash) ───────────
        if (order.DiscountAmount > 0)
            lines.Add((salesDiscAcct, order.DiscountAmount, 0, $"خصم ممنوح - {order.OrderNumber} (مدين)"));

        var netReceivable = order.TotalAmount; 
        
        // POS is usually paid instantly unless credit. Website is paid if Paid (Online/COD delivered).
        bool isCashCollection = (order.Source == OrderSource.POS && order.PaymentStatus == PaymentStatus.Paid) ||
                                (order.Source == OrderSource.Website && order.PaymentMethod != PaymentMethod.Credit && order.PaymentStatus == PaymentStatus.Paid);

        if (isCashCollection)
        {
            // --- SMART MIXED PAYMENT SPLIT ---
            // Pattern: "Mixed: Cash=500, Card=500" in AdminNotes or description
            var note = order.AdminNotes ?? "";
            bool isMixed = order.PaymentMethod == PaymentMethod.Mixed || note.Contains("Mixed:", StringComparison.OrdinalIgnoreCase);
            
            if (isMixed && note.Contains("Mixed:", StringComparison.OrdinalIgnoreCase))
            {
                // Robust parsing for "Mixed: Cash=100, Bank=200, Vodafone=50, ..."
                decimal cashPart = 0, bankPart = 0, vodafonePart = 0, instaPart = 0;
                var cleanNote = note.Replace("Mixed:", "").Replace("/", " ").Replace("-", " ");
                var pairs = cleanNote.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var pair in pairs)
                {
                    var kv = pair.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
                    if (kv.Length < 2) continue;
                    var k = kv[0].Trim().ToLower();
                    var vStr = kv[1].Trim();
                    if (!decimal.TryParse(vStr, out var v)) continue;

                    if (k.Contains("cash")) cashPart += v;
                    else if (k.Contains("bank") || k.Contains("card") || k.Contains("visa") || k.Contains("terminal")) bankPart += v;
                    else if (k.Contains("vodafone")) vodafonePart += v;
                    else if (k.Contains("instapay") || k.Contains("insta")) instaPart += v;
                }

                if (cashPart > 0) {
                    var code = GetMappedCashAccount(PaymentMethod.Cash, order.Source, mapDict);
                    lines.Add((code, cashPart, 0, $"تحصيل (كاش مختلط) - {order.OrderNumber}"));
                }
                if (bankPart > 0) {
                    var code = GetMappedCashAccount(PaymentMethod.CreditCard, order.Source, mapDict);
                    lines.Add((code, bankPart, 0, $"تحصيل (بنك مختلط) - {order.OrderNumber}"));
                }
                if (vodafonePart > 0) {
                    var code = GetMappedCashAccount(PaymentMethod.Vodafone, order.Source, mapDict);
                    lines.Add((code, vodafonePart, 0, $"تحصيل (فودافون مختلط) - {order.OrderNumber}"));
                }
                if (instaPart > 0) {
                    var code = GetMappedCashAccount(PaymentMethod.InstaPay, order.Source, mapDict);
                    lines.Add((code, instaPart, 0, $"تحصيل (إنستاباي مختلط) - {order.OrderNumber}"));
                }
                
                // Final residual reconciliation (ensures Journal balance match)
                var parsedSum = cashPart + bankPart + vodafonePart + instaPart;
                var residual  = netReceivable - parsedSum;
                if (Math.Abs(residual) > 0.01m) {
                     var fallback = GetMappedCashAccount(PaymentMethod.Cash, order.Source, mapDict);
                     lines.Add((fallback, residual, 0, $"تحصيل (متبقي الصندوق) - {order.OrderNumber}"));
                }
            }
            else 
            {
                // Standard Single-Method Collection
                var cashCode = GetMappedCashAccount(order.PaymentMethod, order.Source, mapDict);
                var sourceName = order.Source == OrderSource.POS ? "كاشير" : "موقع";
                lines.Add((cashCode, netReceivable, 0, $"تحصيل نقدي {sourceName} - {order.OrderNumber} (مدين)"));
            }
        }
        else
        {
            lines.Add((receivablesAcct, netReceivable, 0, $"مستحق على العميل - {order.OrderNumber} (مدين)"));
        }

        // ── 3. COGS / Inventory (Continuous Inventory) ─────────
        var totalCost = order.Items?.Sum(i => (i.Product?.CostPrice ?? 0) * i.Quantity) ?? 0;
        if (totalCost > 0)
        {
            lines.Add((cogsAcct, totalCost, 0,      $"تكلفة المبيعات - {order.OrderNumber} (مدين)"));
            lines.Add((inventoryAcct, 0, totalCost, $"خروج مخزون - {order.OrderNumber} (دائن)"));
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

        var mappings = await _db.AccountSystemMappings.Where(m => !m.IsDeleted).ToListAsync();
        var mapDict  = mappings.ToDictionary(m => m.Key.ToLower(), m => m.AccountId);

        string salesReturnAcct = GetMap(mapDict, "salesReturnAccountID", SALES_RETURN);
        string receivablesAcct = GetMap(mapDict, "customerAccountID", RECEIVABLES);
        string inventoryAcct   = GetMap(mapDict, "inventoryAccountID", INVENTORY);
        string cogsAcct        = GetMap(mapDict, "costOfGoodsSoldAccountID", COGS);

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        // 1. Separate Net Return and VAT based on Snapshots
        var totalVatAmount = order.TotalVatAmount;
        var netReturnPrice = order.TotalAmount - totalVatAmount;

        // 2. Financial Reversal
        lines.Add((salesReturnAcct, netReturnPrice, 0, $"مرتجع مبيعات (صافي) - {order.OrderNumber}"));
        
        if (totalVatAmount > 0)
        {
            const string VAT_PAYABLE = "2221"; // Standard fallback code
            string vatAcct = GetMap(mapDict, "vatPayableAccountID", VAT_PAYABLE);
            lines.Add((vatAcct, totalVatAmount, 0, $"إلغاء ضريبة قيمة مضافة - {order.OrderNumber}"));
        }

        lines.Add((receivablesAcct, 0, order.TotalAmount, $"دائن العميل (إجمالي) - {order.OrderNumber}"));

        // 3. Reversal of COGS (Stock back to Inventory)
        var totalCost = order.Items?.Sum(i => (i.Product?.CostPrice ?? 0) * i.Quantity) ?? 0;
        if (totalCost > 0)
        {
            lines.Add((inventoryAcct, totalCost, 0,         $"إعادة للمخزون - {order.OrderNumber}"));
            lines.Add((cogsAcct,      0,         totalCost, $"تخفيض تكلفة المبيعات - {order.OrderNumber}"));
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
    // 2.5 تحصيل مالي لطلب (تحويل من ذمم مدينة إلى نقدية)
    // ══════════════════════════════════════════════════════
    public async Task PostOrderPaymentAsync(Order order)
    {
        // تمييز رمز التحصيل لمنع التكرار
        var reference = order.OrderNumber + "-PMT";
        if (await EntryExists(JournalEntryType.ReceiptVoucher, reference)) return;

        // جلب الإحصائيات والربط
        var mappings = await _db.AccountSystemMappings.Where(m => !m.IsDeleted).ToListAsync();
        var mapDict  = mappings.ToDictionary(m => m.Key.ToLower(), m => m.AccountId);

        string receivablesAcct = GetMap(mapDict, "customerAccountID", RECEIVABLES);
        var cashCode = GetMappedCashAccount(order.PaymentMethod, order.Source, mapDict);

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        // القيد: من حساب النقدية/البنك إلى حساب العملاء
        lines.Add((cashCode,        order.TotalAmount, 0,                $"تحصيل طلب {order.OrderNumber} ({order.PaymentMethod})"));
        lines.Add((receivablesAcct, 0,                order.TotalAmount, $"إغلاق مديونية طلب {order.OrderNumber}"));

        await PostEntry(
            type:        JournalEntryType.ReceiptVoucher,
            reference:   reference,
            description: $"تحصيل تلقائي للطلب {order.OrderNumber} - {order.Customer?.FullName}",
            date:        DateTime.UtcNow,
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId,
            source:      order.Source
        );
    }

    // ══════════════════════════════════════════════════════
    // 2.6 رد مالي لطلب (تحويل من نقدية إلى ذمم مدينة)
    // ══════════════════════════════════════════════════════
    public async Task PostOrderRefundAsync(Order order)
    {
        var reference = order.OrderNumber + "-RFD";
        if (await EntryExists(JournalEntryType.PaymentVoucher, reference)) return;

        var mappings = await _db.AccountSystemMappings.Where(m => !m.IsDeleted).ToListAsync();
        var mapDict  = mappings.ToDictionary(m => m.Key.ToLower(), m => m.AccountId);

        string receivablesAcct = GetMap(mapDict, "customerAccountID", RECEIVABLES);
        var cashCode = GetMappedCashAccount(order.PaymentMethod, order.Source, mapDict);

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        // القيد: من حساب العملاء إلى حساب النقدية (رد المبلغ)
        lines.Add((receivablesAcct, order.TotalAmount, 0,                $"رد مديونية لطلب {order.OrderNumber}"));
        lines.Add((cashCode,        0,                order.TotalAmount, $"رد مبلغ الطلب {order.OrderNumber} ({order.PaymentMethod})"));

        await PostEntry(
            type:        JournalEntryType.PaymentVoucher,
            reference:   reference,
            description: $"رد تلقائي للطلب {order.OrderNumber} - {order.Customer?.FullName}",
            date:        DateTime.UtcNow,
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId,
            source:      order.Source
        );
    }

    // ══════════════════════════════════════════════════════
    // 3. فاتورة مشتريات
    // ══════════════════════════════════════════════════════
    public async Task PostPurchaseInvoiceAsync(PurchaseInvoice invoice)
    {
        if (await EntryExists(JournalEntryType.PurchaseInvoice, invoice.InvoiceNumber)) return;

        var mappings = await _db.AccountSystemMappings.Where(m => !m.IsDeleted).ToListAsync();
        var mapDict  = mappings.ToDictionary(m => m.Key.ToLower(), m => m.AccountId);

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        // ── 1. Debits: Inventory + VAT ─────────────────────────────
        var invAcct = invoice.InventoryAccountId?.ToString() ?? GetMap(mapDict, "inventoryAccountID", INVENTORY);
        lines.Add((invAcct, invoice.SubTotal, 0, $"مشتريات بضاعة - {invoice.InvoiceNumber}"));

        if (invoice.TaxAmount > 0)
        {
            var vatAcct = invoice.VatAccountId?.ToString() ?? GetMap(mapDict, "vatInputAccountID", VAT_INPUT);
            lines.Add((vatAcct, invoice.TaxAmount, 0, $"ضريبة مشتريات (مدخلات) - {invoice.InvoiceNumber}"));
        }

        // ── 2. Credits: Vendor + Discount ───────────────────────────
        var vendorAcct = invoice.VendorAccountId?.ToString() 
                        ?? invoice.Supplier?.MainAccountId?.ToString() 
                        ?? GetMap(mapDict, "supplierAccountID", PAYABLES);
        lines.Add((vendorAcct, 0, invoice.TotalAmount, $"إثبات استحقاق للمورد - {invoice.InvoiceNumber}"));

        // ── 3. Immediate Cash payment (If terms=Cash) ───────────────
        if (invoice.PaymentTerms == PaymentTerms.Cash)
        {
            var cashAcct = invoice.CashAccountId?.ToString() ?? GetMap(mapDict, "cashAccountID", CASH_ACCOUNTS);
            lines.Add((vendorAcct, invoice.TotalAmount, 0, $"سداد مورد فوري - {invoice.InvoiceNumber}"));
            lines.Add((cashAcct, 0, invoice.TotalAmount, $"خروج نقدية مشتريات - {invoice.InvoiceNumber}"));
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

        var mappings = await _db.AccountSystemMappings.Where(m => !m.IsDeleted).ToListAsync();
        var mapDict  = mappings.ToDictionary(m => m.Key.ToLower(), m => m.AccountId);

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        var vendorAcct = invoice.VendorAccountId?.ToString() 
                        ?? invoice.Supplier?.MainAccountId?.ToString() 
                        ?? GetMap(mapDict, "supplierAccountID", PAYABLES);
        var rtnAcct    = invoice.ExpenseAccountId?.ToString() ?? GetMap(mapDict, "purchaseReturnAccountID", PURCHASES_NET);
        var vatAcct    = invoice.VatAccountId?.ToString() ?? GetMap(mapDict, "vatInputAccountID", VAT_INPUT);

        lines.Add((vendorAcct, invoice.TotalAmount, 0,                $"رد للمورد - {invoice.InvoiceNumber}"));
        lines.Add((rtnAcct,    0,                  invoice.SubTotal,  $"مرتجع مشتريات - {invoice.InvoiceNumber}"));

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

    private string GetMap(Dictionary<string, int?> map, string key, string fallback)
    {
        if (map.TryGetValue(key.ToLower(), out var id) && id.HasValue)
            return id.Value.ToString();
        return fallback;
    }

    /// Choosing the cash account based on payment method, source, and stored mappings
    private string GetMappedCashAccount(PaymentMethod method, OrderSource source, Dictionary<string, int?> map)
    {
        string? key = (method, source) switch
        {
            (PaymentMethod.Vodafone, OrderSource.POS)     => "posVodafoneAccountID",
            (PaymentMethod.Vodafone, OrderSource.Website) => "webVodafoneAccountID",
            (PaymentMethod.InstaPay, OrderSource.POS)     => "posInstapayAccountID",
            (PaymentMethod.InstaPay, OrderSource.Website) => "webInstapayAccountID",
            (PaymentMethod.CreditCard, OrderSource.POS)   => "posBankAccountID",
            (PaymentMethod.CreditCard, OrderSource.Website) => "webBankAccountID",
            (PaymentMethod.Cash, OrderSource.POS)         => "posCashAccountID",
            (PaymentMethod.Cash, OrderSource.Website)     => "webCashAccountID",
            _ => null
        };

        if (key != null && map.TryGetValue(key.ToLower(), out var mappedId) && mappedId.HasValue)
            return mappedId.Value.ToString();

        // Fallback to main cash account
        if (map.TryGetValue("cashaccountid", out var mainCashId) && mainCashId.HasValue)
            return mainCashId.Value.ToString();

        // Ultimate hardcoded fallbacks
        return (method, source) switch
        {
            (PaymentMethod.Vodafone,   OrderSource.POS)     => VODAFONE,
            (PaymentMethod.Vodafone,   OrderSource.Website) => VODAFONE_WEB,
            (PaymentMethod.InstaPay,   OrderSource.POS)     => INSTAPAY,
            (PaymentMethod.InstaPay,   OrderSource.Website) => INSTAPAY_WEB,
            (PaymentMethod.CreditCard, _)                   => BANK,
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
