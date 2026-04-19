using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using MK = Sportive.API.Utils.MappingKeys;

namespace Sportive.API.Services;

/// <summary>
/// خدمة مخصصة لقيود المبيعات: فواتير البيع + المرتجعات
/// تعتمد على AccountingCoreService للـ helpers المشتركة
/// </summary>
public class SalesAccountingService
{
    private readonly AppDbContext _db;
    private readonly AccountingCoreService _core;
    private readonly ILogger<SalesAccountingService> _logger;

    public SalesAccountingService(
        AppDbContext db,
        AccountingCoreService core,
        ILogger<SalesAccountingService> logger)
    {
        _db   = db;
        _core = core;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════
    // فاتورة مبيعات — Invoice
    // ══════════════════════════════════════════════════════
    public async Task PostSalesOrderAsync(Order order)
    {
        // 🚨 AUTO-UPDATE: حذف القيد القديم إن وجد للسماح بالتحديث التلقائي عند تعديل الطلب
        var existing = await _db.JournalEntries
            .FirstOrDefaultAsync(e => e.Type == JournalEntryType.SalesInvoice && e.Reference == order.OrderNumber);
        
        if (existing != null)
        {
            _db.JournalEntries.Remove(existing);
            await _db.SaveChangesAsync();
        }

        var store  = await _db.StoreInfo.FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        var vatRate = (store?.VatRatePercent ?? 14) / 100m;

        var mapDict = await _core.GetSafeSystemMappingsAsync();

        string salesRevAcct  = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Sales, mapDict)}";
        string salesDiscAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.SalesDiscount, mapDict)}";
        string inventoryAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Inventory, mapDict)}";
        string cogsAcct      = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.COGS, mapDict)}";

        // ── Customer Account ─────────────────────────────────
        string receivablesAcct;
        if (order.Customer?.MainAccountId != null)
        {
            receivablesAcct = $"ID:{order.Customer.MainAccountId}";
        }
        else
        {
            receivablesAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Customer, mapDict)}";
        }

        string deliveryRevAcct = !string.IsNullOrEmpty(store?.DeliveryRevenueAccountId)
            ? $"ID:{store.DeliveryRevenueAccountId}"
            : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.DeliveryRevenue, mapDict)}";

        string vatAcct = !string.IsNullOrEmpty(store?.StoreVatAccountId)
            ? $"ID:{store.StoreVatAccountId}"
            : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.VatOutput, mapDict)}";

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        // ── 1. Credits: Revenue + VAT + Delivery ─────────────
        decimal totalVatAmount  = 0;
        decimal totalGrossRevenue = 0;
        decimal totalItemDiscounts = 0;

        if (order.Items != null && order.Items.Any())
        {
            foreach (var item in order.Items)
            {
                decimal rate = (item.VatRateApplied ?? 14) / 100m;
                
                // Gross Revenue (before any discount, but Net of VAT calculation)
                // However, the cleanest way: TotalGross = sum(OriginalUnitPrice * Qty) - sum(Vat on original if needed?)
                // Actually, the user wants Gross Price as revenue.
                // Revenue (Credit) + VAT (Credit) = OriginalPrice * Qty + (Vat on Discounted Price?)
                // No, let's keep it simple:
                // Credit Revenue (Gross Net) = (OriginalTotalPrice) / (1 + rate)
                // Credit VAT = item.ItemVatAmount
                
                decimal itemOriginalTotal = item.OriginalUnitPrice * item.Quantity;
                decimal itemGrossNet = item.HasTax 
                    ? Math.Round(itemOriginalTotal / (1 + rate), 2)
                    : itemOriginalTotal;
                
                totalGrossRevenue += itemGrossNet;
                totalVatAmount    += item.ItemVatAmount;
                totalItemDiscounts += item.DiscountAmount;
            }
        }
        else
        {
            totalGrossRevenue = Math.Round(order.SubTotal / (1 + vatRate), 2);
            totalVatAmount    = order.SubTotal - totalGrossRevenue;
        }

        lines.Add((salesRevAcct, 0, totalGrossRevenue, $"مبيعات - {order.OrderNumber} (إجمالي الإيراد)"));
        if (totalVatAmount > 0)
            lines.Add((vatAcct, 0, totalVatAmount, $"ضريبة مبيعات {store?.VatRatePercent ?? 14}% - {order.OrderNumber}"));
        
        if (order.DeliveryFee > 0)
        {
            lines.Add((deliveryRevAcct, 0, order.DeliveryFee, $"إيراد توصيل - {order.OrderNumber}"));
        }
        else if (order.FulfillmentType == FulfillmentType.Delivery && !string.IsNullOrEmpty(order.DeliveryAddress?.City))
        {
            // ✅ Free Shipping Logic: record as revenue vs discount if it matched a zone
            var city = order.DeliveryAddress.City.Trim().ToLower();
            var matchedZone = (await _db.ShippingZones.AsNoTracking().ToListAsync())
                .FirstOrDefault(z => z.IsActive && z.Governorates.ToLower().Split(',').Any(g => g.Trim() == city));
            
            if (matchedZone != null && matchedZone.Fee > 0)
            {
                lines.Add((deliveryRevAcct, 0, matchedZone.Fee, $"إيراد توصيل مهدي (مجاني) - {order.OrderNumber}"));
                lines.Add((salesDiscAcct, matchedZone.Fee, 0, $"خصم شحن مجاني - {order.OrderNumber}"));
            }
        }

        // ── 2. Debits: Discount + Cash/Credit Routing ─────────
        // ⚠️ FIX: Prevent double-counting of discounts in the journal.
        // order.DiscountAmount = itemDiscounts + manualCashierDiscount
        // But totalGrossRevenue is computed from OriginalUnitPrice (before item discounts),
        // so item discounts are already reflected as the gap between gross revenue and actual price.
        // We must debit each component separately to keep the journal balanced:
        //   (a) totalItemDiscounts  → auto item/time-based discounts
        //   (b) manualDiscount      → cashier manual discount (على مستوى الفاتورة)
        decimal manualDiscount = Math.Round(Math.Max(0m, order.DiscountAmount - totalItemDiscounts), 2);

        if (totalItemDiscounts > 0)
            lines.Add((salesDiscAcct, totalItemDiscounts, 0, $"خصم الأصناف التلقائي - {order.OrderNumber}"));

        if (manualDiscount > 0)
            lines.Add((salesDiscAcct, manualDiscount, 0, $"خصم يدوي (كاشير) - {order.OrderNumber}"));

        // ✅ ROBUSTNESS: Ensure payments are loaded and fresh
        if (order.Payments == null || !order.Payments.Any())
        {
            await _db.Entry(order).Collection(o => o.Payments).LoadAsync();
        }

        decimal handledPaidAmt = 0;
        var payments = order.Payments?.Where(p => p.Amount > 0 && p.Method != PaymentMethod.Credit).ToList()
                    ?? new List<OrderPayment>();

        if (payments.Any())
        {
            foreach (var p in payments)
            {
                var cashAcct = await _core.GetMappedCashAccountAsync(p.Method, order.Source, mapDict);
                string methodAr = _core.GetMethodLabel(p.Method);
                lines.Add((cashAcct, p.Amount, 0, $"تحصيل ({methodAr}) - {order.OrderNumber}"));
                handledPaidAmt += p.Amount;
            }
        }
        else
        {
            // Legacy Note Parsing Fallback (Only for older orders)
            var splits = _core.ParseMixedPayments(order.AdminNotes);
            if (splits.Count > 0)
            {
                foreach (var (m, v) in splits)
                {
                    var cashAcct = await _core.GetMappedCashAccountAsync(m, order.Source, mapDict);
                    lines.Add((cashAcct, v, 0, $"تحصيل ({_core.GetMethodLabel(m)}) - {order.OrderNumber}"));
                    handledPaidAmt += v;
                }
            }
            else if (order.PaymentMethod != PaymentMethod.Credit && order.PaidAmount > 0)
            {
                var cashAcct = await _core.GetMappedCashAccountAsync(order.PaymentMethod, order.Source, mapDict);
                decimal payAmt = order.PaidAmount;
                lines.Add((cashAcct, payAmt, 0, $"تحصيل ({_core.GetMethodLabel(order.PaymentMethod)}) - {order.OrderNumber}"));
                handledPaidAmt = payAmt;
            }
        }

        // ⚠️ STRICT VALIDATION: No silent adjustments or magic fixes.
        // If the calculated payment (handledPaidAmt) doesn't match the order's PaidAmount, we fail.
        if (Math.Abs(handledPaidAmt - order.PaidAmount) > 0.01m)
        {
            throw new InvalidOperationException($"خطأ في مطابقة الدفع: المبلغ المسجل في الطلب ({order.PaidAmount}) لا يطابق مجموع بنود الدفع في الحسابات ({handledPaidAmt}). " +
                "يرجى مراجعة تفاصيل الدفع للطلب وتصحيحها قبل الترحيل.");
        }

        // Remaining debt → Receivables
        var remainingDebt = Math.Round(order.TotalAmount - handledPaidAmt, 2);


        if (Math.Abs(remainingDebt) > 0.01m)
            lines.Add((receivablesAcct, remainingDebt, 0, $"إثبات مديونية متبقية (آجل) - {order.OrderNumber}"));

        // ── 2.5 Final Balancing Check ────────────────────────
        // Ensure mathematically balanced entry (absorb tiny rounding diffs into revenue if any)
        decimal sumDr = lines.Sum(l => l.debit);
        decimal sumCr = lines.Sum(l => l.credit);
        decimal diff = sumDr - sumCr;
        
        if (Math.Abs(diff) > 0 && Math.Abs(diff) < 0.1m)
        {
            // Adjust the sales revenue line by the diff to ensure perfect balance
            var revLineIdx = lines.FindIndex(l => l.code == salesRevAcct);
            if (revLineIdx != -1)
            {
                var target = lines[revLineIdx];
                lines[revLineIdx] = (target.code, target.debit, target.credit + diff, target.desc);
            }
        }

        // ── 3. COGS / Inventory ───────────────────────────────
        var totalCost = order.Items?.Sum(i => (i.Product?.CostPrice ?? 0) * i.Quantity) ?? 0;
        if (totalCost > 0)
        {
            lines.Add((cogsAcct,      totalCost, 0,         $"تكلفة المبيعات - {order.OrderNumber}"));
            lines.Add((inventoryAcct, 0,         totalCost, $"خروج مخزون - {order.OrderNumber}"));
        }

        var entry = await _core.PostEntryAsync(
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
    // مرتجع مبيعات كامل
    // ══════════════════════════════════════════════════════
    public async Task PostSalesReturnAsync(Order order)
    {
        if (await _core.EntryExistsAsync(JournalEntryType.SalesReturn, order.OrderNumber + "-RTN")) return;

        var mapDict = await _core.GetSafeSystemMappingsAsync();

        string salesReturnAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.SalesReturn, mapDict)}";
        string receivablesAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Customer,    mapDict)}";
        string inventoryAcct   = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Inventory,   mapDict)}";
        string cogsAcct        = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.COGS,        mapDict)}";

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        var totalVatAmount = order.TotalVatAmount;
        var netReturnPrice = order.TotalAmount - totalVatAmount;

        lines.Add((salesReturnAcct, netReturnPrice, 0, $"مرتجع مبيعات (صافي) - {order.OrderNumber}"));
        if (totalVatAmount > 0)
        {
            string vatAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.VatOutput, mapDict)}";
            lines.Add((vatAcct, totalVatAmount, 0, $"إلغاء ضريبة مبيعات - {order.OrderNumber}"));
        }

        if (order.PaymentStatus == PaymentStatus.Paid || order.Source == OrderSource.POS)
        {
            var cashCode = await _core.GetMappedCashAccountAsync(order.PaymentMethod, order.Source, mapDict);
            lines.Add((cashCode, 0, order.TotalAmount, $"رد نقدي للمرتجع ({_core.GetMethodLabel(order.PaymentMethod)}) - {order.OrderNumber}"));
        }
        else
        {
            lines.Add((receivablesAcct, 0, order.TotalAmount, $"تنزيل من مديونية العميل (مرتجع آجل) - {order.OrderNumber}"));
        }

        var totalCost = order.Items?.Sum(i => (i.Product?.CostPrice ?? 0) * i.Quantity) ?? 0;
        if (totalCost > 0)
        {
            lines.Add((inventoryAcct, totalCost, 0,         $"إعادة للمخزون - {order.OrderNumber}"));
            lines.Add((cogsAcct,      0,         totalCost, $"تخفيض تكلفة المبيعات - {order.OrderNumber}"));
        }

        await _core.PostEntryAsync(
            type:        JournalEntryType.SalesReturn,
            reference:   order.OrderNumber + "-RTN",
            description: $"مرتجع مبيعات موحد {order.OrderNumber}",
            date:        TimeHelper.GetEgyptTime(),
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId,
            source:      order.Source
        );
    }

    // ══════════════════════════════════════════════════════
    // مرتجع مبيعات جزئي
    // ══════════════════════════════════════════════════════
    public async Task PostPartialSalesReturnAsync(Order order, List<OrderItem> returnedItems, decimal refundAmount, int? refundAccountId = null)
    {
        var suffix    = TimeHelper.GetEgyptTime().Ticks.ToString().Substring(10);
        var reference = $"{order.OrderNumber}-PRT-{suffix}";

        var mapDict = await _core.GetSafeSystemMappingsAsync();

        string salesReturnAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.SalesReturn,    mapDict)}";
        string salesDiscAcct   = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.SalesDiscount,  mapDict)}";
        string receivablesAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Customer,       mapDict)}";
        string inventoryAcct   = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Inventory,      mapDict)}";
        string cogsAcct        = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.COGS,           mapDict)}";

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        decimal totalNetReturn  = 0;
        decimal totalVatReturn  = 0;
        decimal totalCostReturn = 0;

        foreach (var item in returnedItems)
        {
            var net = item.TotalPrice - item.ItemVatAmount;
            totalNetReturn  += net;
            totalVatReturn  += item.ItemVatAmount;
            totalCostReturn += (item.Product?.CostPrice ?? 0) * item.Quantity;
        }

        lines.Add((salesReturnAcct, totalNetReturn, 0, $"مرتجع جزئي (صافي) - {order.OrderNumber}"));
        if (totalVatReturn > 0)
        {
            string vatAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.VatOutput, mapDict)}";
            lines.Add((vatAcct, totalVatReturn, 0, $"إلغاء ضريبة جزئية - {order.OrderNumber}"));
        }

        decimal discountReversal = (totalNetReturn + totalVatReturn) - refundAmount;
        if (discountReversal > 0)
            lines.Add((salesDiscAcct, 0, discountReversal, $"موازنة خصم (مرتجع جزئي) - {order.OrderNumber}"));
        else if (discountReversal < 0)
            lines.Add((salesDiscAcct, Math.Abs(discountReversal), 0, $"تسوية تفاوت (مرتجع جزئي) - {order.OrderNumber}"));

        if (order.PaymentStatus == PaymentStatus.Paid || order.Source == OrderSource.POS)
        {
            string cashId = refundAccountId.HasValue
                ? $"ID:{refundAccountId.Value}"
                : await _core.GetMappedCashAccountAsync(order.PaymentMethod, order.Source, mapDict);
            lines.Add((cashId, 0, refundAmount, $"رد نقدية جزئي ({_core.GetMethodLabel(order.PaymentMethod)}) - {order.OrderNumber}"));
        }
        else
        {
            lines.Add((receivablesAcct, 0, refundAmount, $"تنزيل مديونية جزئي - {order.OrderNumber}"));
        }

        if (totalCostReturn > 0)
        {
            lines.Add((inventoryAcct, totalCostReturn, 0,              $"إعادة للمخزون (جزئي) - {order.OrderNumber}"));
            lines.Add((cogsAcct,      0,               totalCostReturn, $"تخفيض تكلفة (جزئي) - {order.OrderNumber}"));
        }

        await _core.PostEntryAsync(
            type:        JournalEntryType.SalesReturn,
            reference:   reference,
            description: $"مرتجع جزئي موحد {order.OrderNumber} ({returnedItems.Count} أصناف)",
            date:        TimeHelper.GetEgyptTime(),
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId,
            source:      order.Source
        );
    }
}
