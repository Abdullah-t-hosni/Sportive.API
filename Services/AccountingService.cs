using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using System.Text.Json;
using Sportive.API.Models;
using System.Collections.Concurrent;
using Sportive.API.DTOs;

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
    Task PostPartialSalesReturnAsync(Order order, List<OrderItem> returnedItems, decimal refundAmount);
    Task PostSupplierPaymentAsync(SupplierPayment payment);
    Task ReverseEntryAsync(int journalEntryId, string reason);
    Task<JournalEntry> PostManualEntryAsync(CreateJournalEntryDto dto, string? userId);
    Task PostReceiptVoucherAsync(ReceiptVoucher voucher);
    Task PostPaymentVoucherAsync(PaymentVoucher voucher);
    Task<string> GetMappedCashAccount(PaymentMethod method, OrderSource source, Dictionary<string, int?>? map = null);
    Task<decimal> GetAccountBalanceAsync(string code);
    /// <summary>بيجيب رصيد حساب الكاشير في اليوم الحالي فقط (اليوم من منتصف الليل UTC)</summary>
    Task<decimal> GetTodayDrawerBalanceAsync(string cashAccountCode);
}

public class AccountingService : IAccountingService
{
    private readonly AppDbContext _db;
    private static readonly ConcurrentDictionary<string, bool> _activePostings = new();

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
    private const string PURCHASE_DISC   = "51103"; // خصم مكتسب (المشتريات)
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
        
        var mappings = await _db.AccountSystemMappings.ToListAsync();
        // Force keys to lowercase for robust lookup
        var mapDict = mappings.ToDictionary(m => m.Key.ToLower(), m => m.AccountId);

        // --- Ledger Mapping ---
        string salesRevAcct   = GetMap(mapDict, "salesAccountID", SALES_REVENUE);
        string salesDiscAcct  = GetMap(mapDict, "salesDiscountAccountID", SALES_DISCOUNT);
        string inventoryAcct  = GetMap(mapDict, "inventoryAccountID", INVENTORY);
        string cogsAcct       = GetMap(mapDict, "costOfGoodsSoldAccountID", COGS);
        
        // --- CUSTOMER ACCOUNT RESOLUTION ---
        string receivablesAcct = RECEIVABLES; // Default generic
        if (order.Customer?.MainAccountId != null)
        {
            var acc = await _db.Accounts.FindAsync(order.Customer.MainAccountId);
            if (acc != null) receivablesAcct = acc.Code;
        }
        else
        {
            receivablesAcct = GetMap(mapDict, "customerAccountID", RECEIVABLES);
        }
        
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

        // ── 2. Debits (Discount + Receivables/Cash/Mixed) ────────
        if (order.DiscountAmount > 0)
            lines.Add((salesDiscAcct, order.DiscountAmount, 0, $"خصم ممنوح - {order.OrderNumber} (مدين)"));

        var netReceivable = order.TotalAmount; 
        
        // --- SMART CUSTOMER LEDGER ROUTING ---
        // If we have a customer, we ALWAYS record the receivable on them first to ensure it shows in their aging/balance.
        // If it's a cash/paid sale, we will add a second part to "Collect" it.
        
        bool isCredit = order.PaymentMethod == PaymentMethod.Credit || order.PaymentStatus == PaymentStatus.Pending;
        var note = order.AdminNotes ?? "";

        if (isCredit)
        {
            // ── Pure Credit Sale: Customer is Debited
            lines.Add((receivablesAcct, netReceivable, 0, $"مستحق على العميل (آجل) - {order.OrderNumber}"));
        }
        else
        {
            // ── Paid Sale for a Known Customer:
            // 1. Debit Customer (Record the sale in their ledger)
            // 2. Credit Customer & Debit Cash/Bank (Record the immediate payment)
            
            // Step 1: Record in Customer Sub-ledger
            lines.Add((receivablesAcct, netReceivable, 0, $"إثبات مبيعات عميل - {order.OrderNumber}"));
            
            // Step 2 & 3: Handle Payments (Supporting Mixed Payments)
            bool isJsonMixed = note.Trim().StartsWith("{") && note.Contains("mixed");
            var splits = new List<(PaymentMethod method, decimal amount)>();

            if (isJsonMixed)
            {
                try {
                    using var doc = System.Text.Json.JsonDocument.Parse(note);
                    if (doc.RootElement.TryGetProperty("mixed", out var mixedProps)) {
                        foreach (var prop in mixedProps.EnumerateObject()) {
                            var pm = prop.Name.ToLower();
                            if (pm == "credit") continue; // Debt stays as balance

                            var m = pm switch {
                                "cash" => PaymentMethod.Cash,
                                "bank" => PaymentMethod.Bank,
                                "vodafone" => PaymentMethod.Vodafone,
                                "instapay" => PaymentMethod.InstaPay,
                                _ => PaymentMethod.Cash
                            };
                            if (decimal.TryParse(prop.Value.ToString(), out decimal val) && val > 0)
                                splits.Add((m, val));
                        }
                    }
                } catch { /* Fallback */ }
            }

            if (splits.Count > 0)
            {
                foreach (var (m, v) in splits) {
                    string cashAcct = await GetMappedCashAccount(m, order.Source, mapDict);
                    // Money into Bank, Money out of Customer
                    lines.Add((cashAcct, v, 0, $"تحصيل ({m}) - {order.OrderNumber}"));
                    lines.Add((receivablesAcct, 0, v, $"سداد فوري للعميل ({m}) - {order.OrderNumber}"));
                }
            }
            else
            {
                string cashAcct = await GetMappedCashAccount(order.PaymentMethod, order.Source, mapDict);
                // Money into Bank, Money out of Customer
                lines.Add((cashAcct, netReceivable, 0, $"تحصيل مبيعات نقدية - {order.OrderNumber}"));
                lines.Add((receivablesAcct, 0, netReceivable, $"سداد فوري للعميل - {order.OrderNumber}"));
            }
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

        var mappings = await _db.AccountSystemMappings.ToListAsync();
        var mapDict  = mappings.ToDictionary(m => m.Key.ToLower(), m => m.AccountId);

        string salesReturnAcct = GetMap(mapDict, "salesReturnAccountID", SALES_RETURN);
        string receivablesAcct = GetMap(mapDict, "customerAccountID", RECEIVABLES);
        string inventoryAcct   = GetMap(mapDict, "inventoryAccountID", INVENTORY);
        string cogsAcct        = GetMap(mapDict, "costOfGoodsSoldAccountID", COGS);

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        // 1. Separate Net Return and VAT based on Snapshots
        var totalVatAmount = order.TotalVatAmount;
        var netReturnPrice = order.TotalAmount - totalVatAmount;

        // 2. Financial Reversal (Unified Entry Part 1)
        lines.Add((salesReturnAcct, netReturnPrice, 0, $"مرتجع مبيعات (صافي) - {order.OrderNumber}"));
        if (totalVatAmount > 0)
        {
            const string VAT_PAYABLE = "2221"; 
            string vatAcct = GetMap(mapDict, "vatPayableAccountID", VAT_PAYABLE);
            lines.Add((vatAcct, totalVatAmount, 0, $"إلغاء ضريبة مبيعات - {order.OrderNumber}"));
        }

        // 3. Sub-ledger Traceability (Receivables Reversal)
        lines.Add((receivablesAcct, 0, order.TotalAmount, $"تنزيل من مديونية العميل - {order.OrderNumber}"));

        // 4. Cash Refund (Unified Entry Part 2 - Only if it was paid)
        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            var cashCode = await GetMappedCashAccount(order.PaymentMethod, order.Source, mapDict);
            lines.Add((receivablesAcct, order.TotalAmount, 0,                $"رد مرتجع مالي للعميل - {order.OrderNumber}"));
            lines.Add((cashCode,        0,                order.TotalAmount, $"خروج نقدية للمرتجع - {order.OrderNumber} ({order.PaymentMethod})"));
        }

        // 5. Reversal of COGS (Stock back to Inventory)
        var totalCost = order.Items?.Sum(i => (i.Product?.CostPrice ?? 0) * i.Quantity) ?? 0;
        if (totalCost > 0)
        {
            lines.Add((inventoryAcct, totalCost, 0,         $"إعادة للمخزون - {order.OrderNumber}"));
            lines.Add((cogsAcct,      0,         totalCost, $"تخفيض تكلفة المبيعات - {order.OrderNumber}"));
        }

        await PostEntry(
            type:        JournalEntryType.SalesReturn,
            reference:   order.OrderNumber + "-RTN",
            description: $"مرتجع مبيعات موحد {order.OrderNumber}",
            date:        DateTime.UtcNow,
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId,
            source:      order.Source
        );
    }

    public async Task PostPartialSalesReturnAsync(Order order, List<OrderItem> returnedItems, decimal refundAmount)
    {
        // 1. Generate unique reference for this partial return
        var suffix = DateTime.UtcNow.Ticks.ToString().Substring(10);
        var reference = $"{order.OrderNumber}-PRT-{suffix}";

        var mappings = await _db.AccountSystemMappings.ToListAsync();
        var mapDict  = mappings.ToDictionary(m => m.Key.ToLower(), m => m.AccountId);

        string salesReturnAcct = GetMap(mapDict, "salesReturnAccountID", SALES_RETURN);
        string salesDiscAcct   = GetMap(mapDict, "salesDiscountAccountID", SALES_DISCOUNT);
        string receivablesAcct = GetMap(mapDict, "customerAccountID", RECEIVABLES);
        string inventoryAcct   = GetMap(mapDict, "inventoryAccountID", INVENTORY);
        string cogsAcct        = GetMap(mapDict, "costOfGoodsSoldAccountID", COGS);

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        // 2. Calculate Totals
        decimal totalNetReturn = 0;
        decimal totalVatReturn = 0;
        decimal totalCostReturn = 0;

        foreach(var item in returnedItems)
        {
             var net = item.TotalPrice - item.ItemVatAmount;
             totalNetReturn += net;
             totalVatReturn += item.ItemVatAmount;
             totalCostReturn += (item.Product?.CostPrice ?? 0) * item.Quantity;
        }

        // 3. Reversal Lines (Composite Unified Entry)
        lines.Add((salesReturnAcct, totalNetReturn, 0, $"مرتجع جزئي (صافي) - {order.OrderNumber}"));
        if (totalVatReturn > 0)
        {
            const string VAT_PAYABLE = "2221";
            string vatAcct = GetMap(mapDict, "vatPayableAccountID", VAT_PAYABLE);
            lines.Add((vatAcct, totalVatReturn, 0, $"إلغاء ضريبة جزئية - {order.OrderNumber}"));
        }

        // Handle Discount Reversal to Balance Entry precisely
        decimal discountReversal = (totalNetReturn + totalVatReturn) - refundAmount;
        if (discountReversal > 0)
        {
            lines.Add((salesDiscAcct, 0, discountReversal, $"موازنة خصم (مرتجع جزئي) - {order.OrderNumber}"));
        } else if (discountReversal < 0) {
            lines.Add((salesDiscAcct, Math.Abs(discountReversal), 0, $"تسوية تفاوت (مرتجع جزئي) - {order.OrderNumber}"));
        }

        // Sub-ledger credit to customer
        lines.Add((receivablesAcct, 0, refundAmount, $"دائن العميل (مرتجع جزئي) - {order.OrderNumber}"));

        // Cash component (The immediate refund)
        var cashCode = await GetMappedCashAccount(order.PaymentMethod, order.Source, mapDict);
        lines.Add((receivablesAcct, refundAmount, 0,            $"إثبات رد نقدي للعميل - {order.OrderNumber}"));
        lines.Add((cashCode,        0,            refundAmount, $"خروج نقدية للمرتجع - {order.OrderNumber} ({order.PaymentMethod})"));

        // 4. Inventory Reversal
        if (totalCostReturn > 0)
        {
            lines.Add((inventoryAcct, totalCostReturn, 0,           $"إعادة للمخزون (جزئي) - {order.OrderNumber}"));
            lines.Add((cogsAcct,      0,               totalCostReturn, $"تخفيض تكلفة (جزئي) - {order.OrderNumber}"));
        }

        await PostEntry(
            type:        JournalEntryType.SalesReturn,
            reference:   reference,
            description: $"مرتجع جزئي موحد {order.OrderNumber} ({returnedItems.Count} أصناف)",
            date:        DateTime.UtcNow,
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId,
            source:      order.Source
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
        var mappings = await _db.AccountSystemMappings.ToListAsync();
        var mapDict  = mappings.ToDictionary(m => m.Key.ToLower(), m => m.AccountId);

        string receivablesAcct = order.Customer?.MainAccountId != null 
                               ? $"ID:{order.Customer.MainAccountId}" 
                               : GetMap(mapDict, "customerAccountID", RECEIVABLES);
        var cashCode = await GetMappedCashAccount(order.PaymentMethod, order.Source, mapDict);

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

        var mappings = await _db.AccountSystemMappings.ToListAsync();
        var mapDict  = mappings.ToDictionary(m => m.Key.ToLower(), m => m.AccountId);

        string receivablesAcct = GetMap(mapDict, "customerAccountID", RECEIVABLES);
        var cashCode = await GetMappedCashAccount(order.PaymentMethod, order.Source, mapDict);

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
        if (string.IsNullOrEmpty(invoice.InvoiceNumber)) return;

        // Prevent parallel processing for the same invoice number
        var lockKey = $"Purchase-{invoice.InvoiceNumber}";
        if (!_activePostings.TryAdd(lockKey, true)) return;

        try 
        {
            if (await EntryExists(JournalEntryType.PurchaseInvoice, invoice.InvoiceNumber)) return;

            var mappings = await _db.AccountSystemMappings.ToListAsync();
            var mapDict  = mappings.ToDictionary(m => m.Key.ToLower(), m => m.AccountId);

            var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

            // ── 1. Debits: Inventory + VAT ─────────────────────────────
            var invAcct = invoice.InventoryAccountId != null ? $"ID:{invoice.InventoryAccountId}" : GetMap(mapDict, "inventoryAccountID", INVENTORY);
            lines.Add((invAcct, invoice.SubTotal, 0, $"مشتريات بضاعة - {invoice.InvoiceNumber}"));

            if (invoice.TaxAmount > 0)
            {
                var vatAcct = invoice.VatAccountId != null ? $"ID:{invoice.VatAccountId}" : GetMap(mapDict, "vatInputAccountID", VAT_INPUT);
                lines.Add((vatAcct, invoice.TaxAmount, 0, $"ضريبة مشتريات (مدخلات) - {invoice.InvoiceNumber}"));
            }

            // ── 2. Credits: Vendor + Discount ───────────────────────────
            var vendorAcct = invoice.VendorAccountId != null ? $"ID:{invoice.VendorAccountId}" 
                            : invoice.Supplier?.MainAccountId != null ? $"ID:{invoice.Supplier.MainAccountId}" 
                            : GetMap(mapDict, "supplierAccountID", PAYABLES);
            lines.Add((vendorAcct, 0, invoice.TotalAmount, $"إثبات استحقاق للمورد - {invoice.InvoiceNumber}"));

            // ── 3. Immediate Cash payment (If terms=Cash) ───────────────
            if (invoice.PaymentTerms == PaymentTerms.Cash)
            {
                var cashAcct = invoice.CashAccountId != null ? $"ID:{invoice.CashAccountId}" : GetMap(mapDict, "cashAccountID", CASH_ACCOUNTS);
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
        finally
        {
            _activePostings.TryRemove(lockKey, out _);
        }
    }

    // ══════════════════════════════════════════════════════
    // 4. مرتجع مشتريات
    // ══════════════════════════════════════════════════════
    public async Task PostPurchaseReturnAsync(PurchaseInvoice invoice)
    {
        var refNo = invoice.InvoiceNumber + "-RTN";
        if (await EntryExists(JournalEntryType.PurchaseReturn, refNo)) return;

        var mappings = await _db.AccountSystemMappings.ToListAsync();
        var mapDict  = mappings.ToDictionary(m => m.Key.ToLower(), m => m.AccountId);

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        var vendorAcct = invoice.VendorAccountId != null ? $"ID:{invoice.VendorAccountId}" 
                        : invoice.Supplier?.MainAccountId != null ? $"ID:{invoice.Supplier.MainAccountId}" 
                        : GetMap(mapDict, "supplierAccountID", PAYABLES);
        var rtnAcct    = invoice.ExpenseAccountId != null ? $"ID:{invoice.ExpenseAccountId}" : GetMap(mapDict, "purchaseReturnAccountID", PURCHASES_NET);
        var vatAcct    = invoice.VatAccountId != null ? $"ID:{invoice.VatAccountId}" : GetMap(mapDict, "vatInputAccountID", VAT_INPUT);

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
            .FirstOrDefaultAsync(e => e.Id == journalEntryId);

        if (entry == null || entry.Status == JournalEntryStatus.Reversed) return;

        var count  = await _db.JournalEntries.CountAsync() + 1;
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

    // ── MANUAL & VOUCHER POSTING ─────────────────────────

    public async Task<JournalEntry> PostManualEntryAsync(CreateJournalEntryDto dto, string? userId)
    {
        var count = await _db.JournalEntries.CountAsync() + 1;
        var type  = dto.Type ?? JournalEntryType.Manual;
        var prefix = type == JournalEntryType.OpeningBalance ? "OPE" : "JE";
        
        var entry = new JournalEntry
        {
            EntryNumber = $"{prefix}-{DateTime.UtcNow:yy}{count:D5}",
            EntryDate = dto.EntryDate,
            Description = dto.Description,
            Reference = dto.Reference,
            Type = type,
            Status = JournalEntryStatus.Posted,
            CreatedByUserId = userId
        };

        foreach (var l in dto.Lines)
        {
            entry.Lines.Add(new JournalLine
            {
                AccountId = l.AccountId,
                Debit = l.Debit,
                Credit = l.Credit,
                Description = l.Description,
                CustomerId = l.CustomerId,
                SupplierId = l.SupplierId
            });
        }

        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task PostReceiptVoucherAsync(ReceiptVoucher voucher)
    {
        var lines = new List<(string code, decimal debit, decimal credit, string desc)>
        {
            ($"ID:{voucher.CashAccountId}", voucher.Amount, 0, $"سند قبض {voucher.VoucherNumber} - {voucher.Description}"),
            ($"ID:{voucher.FromAccountId}", 0, voucher.Amount, $"من حساب {voucher.FromAccount?.NameAr} - {voucher.VoucherNumber}")
        };

        await PostEntry(JournalEntryType.ReceiptVoucher, voucher.VoucherNumber, voucher.Description ?? "", voucher.VoucherDate, lines, customerId: voucher.CustomerId);
    }

    public async Task PostPaymentVoucherAsync(PaymentVoucher voucher)
    {
        var lines = new List<(string code, decimal debit, decimal credit, string desc)>
        {
            ($"ID:{voucher.ToAccountId}", voucher.Amount, 0, $"سند صرف {voucher.VoucherNumber} - {voucher.Description}"),
            ($"ID:{voucher.CashAccountId}", 0, voucher.Amount, $"من حساب {voucher.CashAccount?.NameAr} - {voucher.VoucherNumber}")
        };

        await PostEntry(JournalEntryType.PaymentVoucher, voucher.VoucherNumber, voucher.Description ?? "", voucher.VoucherDate, lines, supplierId: voucher.SupplierId);
    }

    // ══════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ══════════════════════════════════════════════════════

    private string GetMap(Dictionary<string, int?> map, string key, string fallback)
    {
        if (map.TryGetValue(key.ToLower(), out var id) && id.HasValue)
            return $"ID:{id.Value}";
        return fallback;
    }

    /// Choosing the cash account based on payment method, source, and stored mappings
    public async Task<string> GetMappedCashAccount(PaymentMethod method, OrderSource source, Dictionary<string, int?>? map = null)
    {
        if (map == null)
        {
            var mappings = await _db.AccountSystemMappings.ToListAsync();
            map = mappings.ToDictionary(m => m.Key.ToLower(), m => m.AccountId);
        }

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
            return $"ID:{mappedId.Value}";

        // Fallback to main cash account
        if (map.TryGetValue("cashaccountid", out var mainCashId) && mainCashId.HasValue)
            return $"ID:{mainCashId.Value}";

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

    public async Task<decimal> GetAccountBalanceAsync(string code)
    {
        var accountId = await GetAccountIdAsync(code);
        var balance = await _db.JournalLines
            .Where(l => l.AccountId == accountId)
            .SumAsync(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0;
        return balance;
    }

    /// <summary>
    /// يحسب رصيد درج الكاشير في اليوم الحالي فقط.
    /// يجمع الدبت والكريدت للقيود الصادرة من اليوم الحالي (UTC) فقط.
    /// هذا هو المبلغ الفعلي الموجود في الدرج الآن.
    /// </summary>
    public async Task<decimal> GetTodayDrawerBalanceAsync(string cashAccountCode)
    {
        var accountId = await GetAccountIdAsync(cashAccountCode);
        var todayStart = DateTime.UtcNow.Date; // منتصف الليل اليوم

        var balance = await _db.JournalLines
            .Where(l => l.AccountId == accountId
                     && l.JournalEntry.EntryDate >= todayStart)
            .SumAsync(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0;

        return balance;
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
        // 1. Explicit ID Resolution
        if (input.StartsWith("ID:"))
        {
            if (int.TryParse(input.Substring(3), out var exactId) && exactId > 0)
            {
                var acctById = await _db.Accounts.Where(a => a.Id == exactId && a.IsActive).Select(a => new { a.Id }).FirstOrDefaultAsync();
                if (acctById != null) return acctById.Id;
            }
        }
        else
        {
            // 2. Resolve by Account Code (Constants like '2101')
            var acctByCode = await _db.Accounts
                .Where(a => a.Code == input && a.IsActive)
                .Select(a => new { a.Id })
                .FirstOrDefaultAsync();

            if (acctByCode != null)
                return acctByCode.Id;

            // 3. Last fallback: It might be a bare ID passed from Legacy Code
            if (int.TryParse(input, out var id) && id > 0)
            {
                var acctById = await _db.Accounts
                    .Where(a => a.Id == id && a.IsActive)
                    .Select(a => new { a.Id })
                    .FirstOrDefaultAsync();

                if (acctById != null)
                    return acctById.Id;
            }
        }

        throw new InvalidOperationException($"حساب '{input}' غير موجود أو غير نشط في شجرة الحسابات");
    }

    /// يتحقق إذا كان القيد موجود مسبقاً (يمنع التكرار)
    private async Task<bool> EntryExists(JournalEntryType type, string reference)
    {
        if (string.IsNullOrEmpty(reference)) return false;

        return await _db.JournalEntries
            .AnyAsync(e => e.Type == type 
                         && e.Reference != null 
                         && e.Reference.Trim().ToLower() == reference.Trim().ToLower());
    }

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

        var count   = await _db.JournalEntries.CountAsync() + 1;
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
            OrderId     = orderId,
            CreatedAt   = DateTime.UtcNow,
        };

        foreach (var (code, debit, credit, desc) in lines)
        {
            if (debit == 0 && credit == 0) continue;
            var accountId = await GetAccountIdAsync(code);
            var actualAccount = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId);
            var realCode = actualAccount?.Code ?? "";

            // ⚠️ STRICT ENTITY ROUTING (User Preference):
            // We only attach entity IDs (CustomerId/SupplierId) to their specific trade accounts.
            // Customers -> Receivables (starts with 1103)
            // Suppliers -> Payables (starts with 2101)
            // This ensures clean ledgers for Revenue, Expenses, and Cash accounts.
            bool isReceivables = realCode.StartsWith("1103");
            bool isPayables = realCode.StartsWith("2101");

            entry.Lines.Add(new JournalLine
            {
                AccountId   = accountId,
                Debit       = debit,
                Credit      = credit,
                Description = desc,
                CustomerId  = isReceivables ? customerId : null,
                SupplierId  = isPayables ? supplierId : null,
                OrderId     = orderId,
                CreatedAt   = DateTime.UtcNow,
            });
        }

        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync();
    }
}
