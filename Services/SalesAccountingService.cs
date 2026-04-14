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
        if (await _core.EntryExistsAsync(JournalEntryType.SalesInvoice, order.OrderNumber)) return;

        var store  = await _db.StoreInfo.FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        var vatRate = (store?.VatRatePercent ?? 14) / 100m;

        var mapDict = await _core.GetSafeSystemMappingsAsync();

        string salesRevAcct  = _core.GetMap(mapDict, MK.Sales,         AccountingCoreService.SALES_REVENUE);
        string salesDiscAcct = _core.GetMap(mapDict, MK.SalesDiscount, AccountingCoreService.SALES_DISCOUNT);
        string inventoryAcct = _core.GetMap(mapDict, MK.Inventory,     AccountingCoreService.INVENTORY);
        string cogsAcct      = _core.GetMap(mapDict, MK.COGS,          AccountingCoreService.COGS);

        // ── Customer Account ─────────────────────────────────
        string receivablesAcct = AccountingCoreService.RECEIVABLES;
        if (order.Customer?.MainAccountId != null)
        {
            var acc = await _db.Accounts.FindAsync(order.Customer.MainAccountId);
            if (acc != null) receivablesAcct = acc.Code;
        }
        else
        {
            receivablesAcct = _core.GetMap(mapDict, MK.Customer, AccountingCoreService.RECEIVABLES);
        }

        string deliveryRevAcct = !string.IsNullOrEmpty(store?.DeliveryRevenueAccountId)
            ? store.DeliveryRevenueAccountId
            : _core.GetMap(mapDict, MK.DeliveryRevenue, AccountingCoreService.DELIVERY_REVENUE);

        string vatAcct = !string.IsNullOrEmpty(store?.StoreVatAccountId)
            ? store.StoreVatAccountId
            : _core.GetMap(mapDict, MK.VatOutput, AccountingCoreService.VAT_OUTPUT);

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        // ── 1. Credits: Revenue + VAT + Delivery ─────────────
        decimal totalVatAmount  = 0;
        decimal totalNetRevenue = 0;

        if (order.Items != null && order.Items.Any())
        {
            foreach (var item in order.Items)
            {
                decimal itemNet = item.TotalPrice;
                decimal itemVat = 0;
                if (item.HasTax)
                {
                    var rate = (item.VatRateApplied ?? 14) / 100m;
                    itemNet = Math.Round(item.TotalPrice / (1 + rate), 2);
                    itemVat = item.TotalPrice - itemNet;
                }
                totalNetRevenue += itemNet;
                totalVatAmount  += itemVat;
            }
        }
        else
        {
            totalNetRevenue = Math.Round(order.SubTotal / (1 + vatRate), 2);
            totalVatAmount  = order.SubTotal - totalNetRevenue;
        }

        lines.Add((salesRevAcct, 0, totalNetRevenue, $"مبيعات - {order.OrderNumber} (الإيراد)"));
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
        if (order.DiscountAmount > 0)
            lines.Add((salesDiscAcct, order.DiscountAmount, 0, $"خصم ممنوح - {order.OrderNumber}"));

        // ✅ NEW: Read from OrderPayments table first, fallback to AdminNotes
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
            // Fallback: legacy AdminNotes JSON or single payment method
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
                decimal payAmt = order.PaymentStatus == PaymentStatus.Paid ? order.TotalAmount : order.PaidAmount;
                lines.Add((cashAcct, payAmt, 0, $"تحصيل ({_core.GetMethodLabel(order.PaymentMethod)}) - {order.OrderNumber}"));
                handledPaidAmt = payAmt;
            }
        }

        // Remaining debt → Receivables
        var remainingDebt = order.TotalAmount - handledPaidAmt;
        if (remainingDebt > 0.01m)
            lines.Add((receivablesAcct, remainingDebt, 0, $"إثبات مديونية متبقية (آجل) - {order.OrderNumber}"));

        // ── 3. COGS / Inventory ───────────────────────────────
        var totalCost = order.Items?.Sum(i => (i.Product?.CostPrice ?? 0) * i.Quantity) ?? 0;
        if (totalCost > 0)
        {
            lines.Add((cogsAcct,      totalCost, 0,         $"تكلفة المبيعات - {order.OrderNumber}"));
            lines.Add((inventoryAcct, 0,         totalCost, $"خروج مخزون - {order.OrderNumber}"));
        }

        await _core.PostEntryAsync(
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

        string salesReturnAcct = _core.GetMap(mapDict, MK.SalesReturn, AccountingCoreService.SALES_RETURN);
        string receivablesAcct = _core.GetMap(mapDict, MK.Customer,    AccountingCoreService.RECEIVABLES);
        string inventoryAcct   = _core.GetMap(mapDict, MK.Inventory,   AccountingCoreService.INVENTORY);
        string cogsAcct        = _core.GetMap(mapDict, MK.COGS,        AccountingCoreService.COGS);

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        var totalVatAmount = order.TotalVatAmount;
        var netReturnPrice = order.TotalAmount - totalVatAmount;

        lines.Add((salesReturnAcct, netReturnPrice, 0, $"مرتجع مبيعات (صافي) - {order.OrderNumber}"));
        if (totalVatAmount > 0)
        {
            string vatAcct = _core.GetMap(mapDict, MK.VatOutput, AccountingCoreService.VAT_OUTPUT);
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

        string salesReturnAcct = _core.GetMap(mapDict, MK.SalesReturn,    AccountingCoreService.SALES_RETURN);
        string salesDiscAcct   = _core.GetMap(mapDict, MK.SalesDiscount,  AccountingCoreService.SALES_DISCOUNT);
        string receivablesAcct = _core.GetMap(mapDict, MK.Customer,       AccountingCoreService.RECEIVABLES);
        string inventoryAcct   = _core.GetMap(mapDict, MK.Inventory,      AccountingCoreService.INVENTORY);
        string cogsAcct        = _core.GetMap(mapDict, MK.COGS,           AccountingCoreService.COGS);

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
            string vatAcct = _core.GetMap(mapDict, MK.VatOutput, AccountingCoreService.VAT_OUTPUT);
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
