using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using Sportive.API.Interfaces;
using Sportive.API.DTOs;
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
    private readonly ITranslator _t;

    public SalesAccountingService(
        AppDbContext db,
        AccountingCoreService core,
        ILogger<SalesAccountingService> logger,
        ITranslator t)
    {
        _db   = db;
        _core = core;
        _logger = logger;
        _t = t;
    }

    // ══════════════════════════════════════════════════════
    // فاتورة مبيعات — Invoice
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    public async Task PostSalesOrderAsync(Order order, DateTime? overrideDate = null)
    {
        if (order.Customer == null && order.CustomerId > 0)
        {
            var found = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CustomerId);
            if (found != null) order.Customer = found;
        }

        if (order.Items == null || !order.Items.Any())
        {
            try { await _db.Entry(order).Collection(o => o.Items).LoadAsync(); } catch { }
        }

        if (order.DeliveryAddress == null)
        {
            try { await _db.Entry(order).Reference(o => o.DeliveryAddress).LoadAsync(); } catch { }
        }

        var store  = await _db.StoreInfo.FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        var vatRate = (store?.VatRatePercent ?? 0) / 100m;

        var mapDict = await _core.GetSafeSystemMappingsAsync();

        string salesRevAcct  = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Sales, mapDict)}";
        string salesDiscAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.SalesDiscount, mapDict)}";
        string inventoryAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Inventory, mapDict)}";
        string cogsAcct      = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.COGS, mapDict)}";

        // ── Employee (Sales Person) ──────────────────────────
        int? employeeId = null;
        if (!string.IsNullOrEmpty(order.SalesPersonId))
        {
            // 1. Check if it's a direct Employee ID (numeric)
            if (int.TryParse(order.SalesPersonId, out int parsedId))
            {
                employeeId = parsedId;
            }
            else 
            {
                // 2. Try direct AppUserId link
                employeeId = await _db.Employees
                    .Where(e => e.AppUserId == order.SalesPersonId)
                    .Select(e => (int?)e.Id)
                    .FirstOrDefaultAsync();

                // 3. Fallback: Try matching by Email if user has one
                if (employeeId == null)
                {
                    var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == order.SalesPersonId);
                    if (user != null && !string.IsNullOrEmpty(user.Email))
                    {
                        employeeId = await _db.Employees
                            .Where(e => e.Email == user.Email)
                            .Select(e => (int?)e.Id)
                            .FirstOrDefaultAsync();
                    }
                }
            }
        }

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
        decimal totalOriginalNetRevenue = 0;
        decimal totalActualVatAmount    = 0;
        decimal totalNetDiscount        = 0;
        decimal totalGrossDiscount      = 0;

        if (order.Items != null && order.Items.Any())
        {
            foreach (var item in order.Items)
            {
                decimal rate = (item.VatRateApplied ?? 0) / 100m;
                
                // Original Gross & Net
                decimal itemOriginalTotal = item.OriginalUnitPrice * item.Quantity;
                decimal itemOriginalNet   = item.HasTax 
                    ? Math.Round(itemOriginalTotal / (1 + rate), 2)
                    : itemOriginalTotal;
                
                // Actual Gross & Net (After discount)
                decimal itemActualTotal   = item.TotalPrice;
                decimal itemActualNet     = item.HasTax
                    ? Math.Round(itemActualTotal / (1 + rate), 2)
                    : itemActualTotal;

                totalOriginalNetRevenue += itemOriginalNet;
                totalActualVatAmount    += item.ItemVatAmount;
                totalNetDiscount        += (itemOriginalNet - itemActualNet);
                totalGrossDiscount      += (itemOriginalTotal - itemActualTotal);
            }
        }
        else
        {
            // Fallback for items-less orders (unlikely in new flow)
            totalOriginalNetRevenue = Math.Round(order.SubTotal / (1 + vatRate), 2);
            totalActualVatAmount    = order.SubTotal - totalOriginalNetRevenue;
        }

        lines.Add((salesRevAcct, 0, totalOriginalNetRevenue, _t.Get("Accounting.SalesRevenueDesc", order.OrderNumber)));
        
        if (totalActualVatAmount > 0)
            lines.Add((vatAcct, 0, totalActualVatAmount, _t.Get("Accounting.SalesTaxDesc", store?.VatRatePercent ?? 0, order.OrderNumber)));
        
        if (order.DeliveryFee > 0)
        {
            lines.Add((deliveryRevAcct, 0, order.DeliveryFee, _t.Get("Accounting.DeliveryRevenueDesc", order.OrderNumber)));
        }
        else if (order.FulfillmentType == FulfillmentType.Delivery && !string.IsNullOrEmpty(order.DeliveryAddress?.City))
        {
            // ✅ Free Shipping Logic: record as revenue vs discount
            // ✅ Optimized: load only active zones with non-zero fees, then match in memory
            //    (avoids full ToListAsync() on every invoice — only zones with IsActive=true and Fee>0)
            var city = order.DeliveryAddress.City.Trim().ToLower();
            var activeZones = await _db.ShippingZones
                .AsNoTracking()
                .Where(z => z.IsActive && z.Fee > 0)
                .Select(z => new { z.Fee, z.Governorates })
                .ToListAsync();
            var matchedZone = activeZones.FirstOrDefault(z => z.Governorates != null && z.Governorates.ToLower().Split(',').Any(g => g.Trim() == city));

            if (matchedZone != null && matchedZone.Fee > 0)
            {
                lines.Add((deliveryRevAcct, 0, matchedZone.Fee, _t.Get("Accounting.FreeShippingRevenueDesc", order.OrderNumber)));
                lines.Add((salesDiscAcct, matchedZone.Fee, 0, _t.Get("Accounting.FreeShippingDiscountDesc", order.OrderNumber)));
            }
        }

        // ── 2. Debits: Discount + Cash/Credit Routing ─────────
        
        // Manual/Coupon discount handling
        decimal manualNetDisc = 0;
        if (order.DiscountAmount > 0)
        {
             // If order has a global discount (manual), we net-ify it to keep math perfect
             manualNetDisc = Math.Round(order.DiscountAmount / (1 + vatRate), 2);
             lines.Add((salesDiscAcct, manualNetDisc, 0, _t.Get("Accounting.ManualDiscountDesc", order.OrderNumber, order.DiscountAmount)));
        }

        // Fix: Subtract manualNetDisc from totalNetDiscount to prevent double-counting distributed global discounts
        decimal remainingPromoDisc = Math.Round(totalNetDiscount - manualNetDisc, 2);
        if (remainingPromoDisc > 0.05m)
        {
            lines.Add((salesDiscAcct, remainingPromoDisc, 0, _t.Get("Accounting.OfferDiscountDesc", order.OrderNumber, totalGrossDiscount)));
        }

        // ✅ ROBUSTNESS: Ensure payments are loaded and fresh
        if (order.Payments == null || !order.Payments.Any())
        {
            await _db.Entry(order).Collection(o => o.Payments).LoadAsync();
        }

        // 🔑 NON-POS PAYMENTS: Website and Admin orders are ALWAYS
        // settled via a SEPARATE ReceiptVoucher (PMT entry) either at Confirmed or Delivered.
        // The SalesInvoice must ALWAYS record the full amount as Debit Customer (receivable debt),
        // regardless of PaidAmount or Payments collection. Embedding cash debits here would cause
        // a double-debit on the cash account when combined with the PMT entry.
        bool isNonPosOrder = order.Source != OrderSource.POS;

        decimal handledPaidAmt = 0;
        var payments = isNonPosOrder
            ? new List<OrderPayment>()  // Always treat as unpaid in SalesInvoice; PMT handles settlement
            : (order.Payments?.Where(p => p.Amount > 0 && p.Method != PaymentMethod.Credit).ToList()
               ?? new List<OrderPayment>());

        if (payments.Any())
        {
            foreach (var p in payments)
            {
                if (p.Method == PaymentMethod.CustomerBalance)
                {
                    // CustomerBalance: Customer uses their stored credit balance to pay.
                    // This is debited directly to the Receivables account (1103), reducing the customer's credit balance.
                    // We do NOT debit POS Cash, to avoid introducing phantom cash in the shift/daily closing.
                    lines.Add((receivablesAcct, p.Amount, 0, "تسديد باستخدام رصيد العميل المتاح"));
                    handledPaidAmt += p.Amount;
                }
                else
                {
                    var cashAcct = await _core.GetMappedCashAccountAsync(p.Method, order.Source, mapDict);
                    string methodLabel = _core.GetMethodLabel(p.Method);
                    lines.Add((cashAcct, p.Amount, 0, _t.Get("Accounting.CollectionDesc", methodLabel, order.OrderNumber)));
                    handledPaidAmt += p.Amount;
                }
            }
        }
        else if (!isNonPosOrder)
        {
            // Legacy Note Parsing Fallback (Only for older orders)
            var splits = _core.ParseMixedPayments(order.AdminNotes);
            if (splits.Count > 0)
            {
                foreach (var (m, v) in splits)
                {
                    var cashAcct = await _core.GetMappedCashAccountAsync(m, order.Source, mapDict);
                    lines.Add((cashAcct, v, 0, _t.Get("Accounting.CollectionDesc", _core.GetMethodLabel(m), order.OrderNumber)));
                    handledPaidAmt += v;
                }
            }
            else if (order.PaymentMethod != PaymentMethod.Credit && order.PaidAmount > 0)
            {
                if (order.PaymentMethod == PaymentMethod.CustomerBalance)
                {
                    lines.Add((receivablesAcct, order.PaidAmount, 0, "تسديد باستخدام رصيد العميل المتاح"));
                }
                else
                {
                    var cashAcct = await _core.GetMappedCashAccountAsync(order.PaymentMethod, order.Source, mapDict);
                    decimal payAmt = order.PaidAmount;
                    lines.Add((cashAcct, payAmt, 0, _t.Get("Accounting.CollectionDesc", _core.GetMethodLabel(order.PaymentMethod), order.OrderNumber)));
                }
                handledPaidAmt = order.PaidAmount;
            }
        }

        // ⚠️ STRICT VALIDATION: No silent adjustments or magic fixes.
        // For website digital payments, handledPaidAmt is intentionally 0 (settled via separate PMT entry).
        decimal expectedHandled = isNonPosOrder ? 0 : order.PaidAmount;
        if (Math.Abs(handledPaidAmt - expectedHandled) > 0.01m)
        {
            throw new InvalidOperationException(_t.Get("Accounting.PaymentMismatchError", order.PaidAmount, handledPaidAmt));
        }

        // Remaining debt → Receivables
        // For website digital payments: full amount is always receivable (settled via PMT entry separately)
        var remainingDebt = isNonPosOrder
            ? Math.Round(order.TotalAmount, 2)
            : Math.Round(order.TotalAmount - handledPaidAmt, 2);

        if (Math.Abs(remainingDebt) > 0.01m)
            lines.Add((receivablesAcct, remainingDebt, 0, _t.Get("Accounting.DebtRecognitionDesc", order.OrderNumber)));

        // ── 2.5 Final Balancing Check ────────────────────────
        decimal sumDr = lines.Sum(l => l.debit);
        decimal sumCr = lines.Sum(l => l.credit);
        decimal diff = sumDr - sumCr;
        
        if (Math.Abs(diff) > 0 && Math.Abs(diff) < 0.1m)
        {
            var revLineIdx = lines.FindIndex(l => l.code == salesRevAcct);
            if (revLineIdx != -1)
            {
                var target = lines[revLineIdx];
                lines[revLineIdx] = (target.code, target.debit, target.credit + diff, target.desc);
            }
        }

        // ── 3. COGS / Inventory ───────────────────────────────
        decimal totalCost = 0;
        if (order.Items != null && order.Items.Any())
        {
            var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
            var productsCost = await _db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.CostPrice);

            totalCost = order.Items.Sum(i => ((i.ProductId.HasValue && productsCost.ContainsKey(i.ProductId.Value)) ? (productsCost[i.ProductId.Value] ?? 0m) : 0m) * i.Quantity);
        }

        if (totalCost > 0)
        {
            lines.Add((cogsAcct,      totalCost, 0,         _t.Get("Accounting.CogsDesc", order.OrderNumber)));
            lines.Add((inventoryAcct, 0,         totalCost, _t.Get("Accounting.InventoryOutDesc", order.OrderNumber)));
        }

        var entryDate = overrideDate ?? TimeHelper.GetEgyptBusinessDayDate(order.CreatedAt);
        var entry = await _core.PostEntryAsync(
            type:        JournalEntryType.SalesInvoice,
            reference:   order.OrderNumber,
            description: _t.Get("Accounting.SalesEntryMainDesc", order.OrderNumber, order.Customer?.FullName ?? ""),
            date:        entryDate,
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId,
            source:      order.Source,
            employeeId:  employeeId,
            createdAt:   entryDate
        );
    }

    public async Task PostSalesReturnAsync(Order order, int? refundAccountId = null, bool refundShipping = false, bool chargeReturnShipping = false, decimal returnShippingFee = 0, bool isReturnedFromCourier = false)
    {
        // ── 1. تأكد من تحميل العناصر والمنتجات ──
        if (order.Items == null || !order.Items.Any())
            try { await _db.Entry(order).Collection(o => o.Items).LoadAsync(); } catch { }

        if (order.Items != null && order.Items.Any(i => i.Product == null))
        {
            try
            {
                var productIds = order.Items.Where(i => i.ProductId.HasValue)
                                            .Select(i => i.ProductId!.Value).Distinct().ToList();
                var products = await _db.Products.AsNoTracking()
                                        .Where(p => productIds.Contains(p.Id)).ToListAsync();
                foreach (var item in order.Items)
                    item.Product ??= products.FirstOrDefault(p => p.Id == item.ProductId);
            }
            catch { }
        }

        // ── 2. جلب الحسابات المحاسبية ──
        var mapDict = await _core.GetSafeSystemMappingsAsync();
        var store   = await _db.StoreInfo.FirstOrDefaultAsync(s => s.StoreConfigId == 1);

        string salesReturnAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.SalesReturn,   mapDict)}";
        string salesDiscAcct   = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.SalesDiscount, mapDict)}";
        string cogsAcct        = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.COGS,          mapDict)}";
        string inventoryAcct   = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Inventory,     mapDict)}";
        string deliveryRevAcct = !string.IsNullOrEmpty(store?.DeliveryRevenueAccountId)
            ? $"ID:{store.DeliveryRevenueAccountId}"
            : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.DeliveryRevenue, mapDict)}";
        string receivablesAcct = order.Customer?.MainAccountId != null
            ? $"ID:{order.Customer.MainAccountId}"
            : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Customer, mapDict)}";

        // ── 3. حساب الأرقام من الأصناف ──
        decimal totalGrossReturn = 0; // السعر الأصلي قبل الخصم  → مدين: مرتجع مبيعات
        decimal totalNetDiscount = 0; // قيمة الخصم الممنوح       → دائن: إلغاء الخصم
        decimal totalNetReturn   = 0; // صافي قيمة البضاعة        → دائن: العملاء
        decimal totalVatReturn   = 0; // الضريبة المضافة           → مدين ثم دائن
        decimal totalCostReturn  = 0; // تكلفة البضاعة             → مدين: مخزن / دائن: COGS

        if (order.Items != null)
        {
            foreach (var item in order.Items)
            {
                decimal rate = (item.VatRateApplied ?? 0) / 100m;
                decimal originalNet = item.HasTax
                    ? Math.Round((item.OriginalUnitPrice * item.Quantity) / (1 + rate), 2)
                    : item.OriginalUnitPrice * item.Quantity;
                decimal actualNet = item.HasTax
                    ? Math.Round(item.TotalPrice / (1 + rate), 2)
                    : item.TotalPrice;

                totalGrossReturn += originalNet;
                totalNetDiscount += originalNet - actualNet;
                totalNetReturn   += actualNet;
                totalVatReturn   += item.ItemVatAmount;
                totalCostReturn  += (item.Product?.CostPrice ?? 0) * item.Quantity;
            }
        }

        // ── 4. بناء القيد المتوازن رياضياً ──
        //
        // مدين  مرتجع مبيعات        = totalGrossReturn
        // مدين  ضريبة المخرجات      = totalVatReturn          [إن وجدت]
        // مدين  إيراد الشحن (إلغاء) = deliveryFeeToRefund     [refundShipping]
        // مدين  مخزن الشحن/الرئيسي  = totalCostReturn
        // مدين  العملاء (رسوم رجوع)  = returnShippingFee       [chargeReturnShipping]
        // ──────────────────────────────────────────
        // دائن  الخصم الممنوح        = totalNetDiscount        [إن وجد]
        // دائن  العملاء (استرداد)    = totalNetReturn + totalVatReturn + deliveryFeeToRefund
        // دائن  تكلفة البضاعة COGS   = totalCostReturn
        // دائن  إيراد التوصيل        = returnShippingFee       [chargeReturnShipping]
        //
        // الإجمالان متساويان دائماً (هوية رياضية) ✓

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        // [ مدين ] مرتجع مبيعات (بالسعر الأصلي قبل الخصم)
        if (totalGrossReturn > 0)
            lines.Add((salesReturnAcct, totalGrossReturn, 0,
                _t.Get("Accounting.SalesReturnDesc", order.OrderNumber)));

        // [ مدين ] ضريبة المخرجات
        if (totalVatReturn > 0)
        {
            string vatAcct = !string.IsNullOrEmpty(store?.StoreVatAccountId)
                ? $"ID:{store.StoreVatAccountId}"
                : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.VatOutput, mapDict)}";
            lines.Add((vatAcct, totalVatReturn, 0,
                _t.Get("Accounting.SalesReturnTaxDesc", order.OrderNumber)));
        }

        // [ مدين ] إلغاء إيراد الشحن (إذا كان المفروض يُرد الشحن للعميل)
        decimal deliveryFeeToRefund = 0;
        if (refundShipping && order.DeliveryFee > 0)
        {
            deliveryFeeToRefund = order.DeliveryFee;
            lines.Add((deliveryRevAcct, deliveryFeeToRefund, 0,
                $"إلغاء إيراد الشحن - طلب #{order.OrderNumber}"));
        }

        // [ مدين ] مخزن شركة الشحن أو المخزن الرئيسي (ترجع البضاعة للمخزن)
        if (totalCostReturn > 0)
        {
            string returnInventoryAcct = inventoryAcct;
            if (isReturnedFromCourier &&
                mapDict.TryGetValue(MK.CourierInventory.ToLower(), out var courierInvId) &&
                courierInvId.HasValue)
            {
                returnInventoryAcct = $"ID:{courierInvId.Value}";
            }
            lines.Add((returnInventoryAcct, totalCostReturn, 0,
                _t.Get("Accounting.InventoryInDesc")));
        }

        // [ مدين ] العملاء - رسوم شحن الإرجاع (ديْن جديد على العميل - مش عيب تصنيع)
        if (chargeReturnShipping && returnShippingFee > 0)
            lines.Add((receivablesAcct, returnShippingFee, 0,
                $"رسوم شحن إرجاع - طلب #{order.OrderNumber}"));

        // [ دائن ] إلغاء الخصم الممنوح أصلاً
        if (totalNetDiscount > 0)
            lines.Add((salesDiscAcct, 0, totalNetDiscount,
                $"إلغاء خصم مبيعات مرتجع - طلب #{order.OrderNumber}"));

        // [ دائن ] العملاء - صافي قيمة البضاعة المرتجعة (يُحسب في رصيد العميل)
        decimal customerCreditAmount = Math.Round(totalNetReturn + totalVatReturn + deliveryFeeToRefund, 2);
        if (customerCreditAmount > 0)
            lines.Add((receivablesAcct, 0, customerCreditAmount,
                _t.Get("Accounting.SalesReturnDebtReductionDesc", order.Customer?.FullName ?? order.OrderNumber)));

        // [ دائن ] عكس تكلفة البضاعة المباعة (COGS)
        if (totalCostReturn > 0)
            lines.Add((cogsAcct, 0, totalCostReturn,
                _t.Get("Accounting.COGSReturnDesc")));

        // [ دائن ] إيراد شحن الإرجاع (في مقابل الرسوم المُحملة على العميل)
        if (chargeReturnShipping && returnShippingFee > 0)
            lines.Add((deliveryRevAcct, 0, returnShippingFee,
                $"إيراد شحن إرجاع - طلب #{order.OrderNumber}"));

        // ── 5. تسجيل القيد ──
        await _core.PostEntryAsync(
            type:        JournalEntryType.SalesReturn,
            reference:   order.OrderNumber + "-RTN",
            description: _t.Get("Accounting.SalesReturnMainDesc", order.OrderNumber, order.Customer?.FullName ?? ""),
            date:        TimeHelper.GetEgyptTime(),
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId,
            source:      order.Source
        );
    }

    public async Task PostPartialSalesReturnAsync(Order order, List<OrderItem> returnedItems, decimal refundAmount, int? refundAccountId = null, bool refundToStoreCredit = false, string? overrideReference = null, DateTime? overrideDate = null, bool chargeReturnShipping = false, decimal returnShippingFee = 0)
    {
        if (order.Customer == null && order.CustomerId > 0)
        {
            var found = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CustomerId);
            if (found != null) order.Customer = found;
        }

        var reference = overrideReference ?? $"{order.OrderNumber}-PRT-{TimeHelper.GetEgyptTime().Ticks.ToString().Substring(10)}";
        var entryDate = overrideDate ?? TimeHelper.GetEgyptTime();

        var mapDict = await _core.GetSafeSystemMappingsAsync();
        var store = await _db.StoreInfo.FirstOrDefaultAsync(s => s.StoreConfigId == 1);

        string salesReturnAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.SalesReturn,    mapDict)}";
        string salesDiscAcct   = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.SalesDiscount,  mapDict)}";
        string deliveryRevAcct = !string.IsNullOrEmpty(store?.DeliveryRevenueAccountId)
            ? $"ID:{store.DeliveryRevenueAccountId}"
            : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.DeliveryRevenue, mapDict)}";
        
        string receivablesAcct;
        if (order.Customer?.MainAccountId != null)
            receivablesAcct = $"ID:{order.Customer.MainAccountId}";
        else
            receivablesAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Customer, mapDict)}";

        string inventoryAcct   = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Inventory,      mapDict)}";
        string cogsAcct        = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.COGS,           mapDict)}";

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        decimal totalGrossReturn = 0;
        decimal totalNetDiscount = 0;
        decimal totalNetReturn  = 0;
        decimal totalVatReturn  = 0;
        decimal totalCostReturn = 0;

        foreach (var item in returnedItems)
        {
            decimal rate = (item.VatRateApplied ?? 0) / 100m;
            decimal origPrice = item.OriginalUnitPrice > 0 ? item.OriginalUnitPrice : (item.UnitPrice > 0 ? item.UnitPrice : (item.Quantity > 0 ? item.TotalPrice / item.Quantity : 0));
            decimal itemActualTotal   = item.TotalPrice;
            decimal itemOriginalTotal = Math.Max(itemActualTotal, origPrice * item.Quantity);
            decimal itemOriginalNet   = item.HasTax ? Math.Round(itemOriginalTotal / (1 + rate), 2) : itemOriginalTotal;
            decimal itemActualNet     = item.HasTax ? Math.Round(itemActualTotal / (1 + rate), 2) : itemActualTotal;

            if (itemOriginalNet < itemActualNet) itemOriginalNet = itemActualNet;

            totalGrossReturn += itemOriginalNet;
            totalNetDiscount += (itemOriginalNet - itemActualNet);
            totalNetReturn   += itemActualNet;
            totalVatReturn   += item.ItemVatAmount;
            totalCostReturn  += (item.Product?.CostPrice ?? 0) * item.Quantity;
        }

        if (totalGrossReturn > 0)
        {
            lines.Add((salesReturnAcct, totalGrossReturn, 0, _t.Get("Accounting.PartialReturnNetDesc", order.OrderNumber)));
        }
        else if (totalNetReturn > 0)
        {
            lines.Add((salesReturnAcct, totalNetReturn, 0, _t.Get("Accounting.PartialReturnNetDesc", order.OrderNumber)));
        }

        if (totalNetDiscount > 0)
        {
            lines.Add((salesDiscAcct, 0, totalNetDiscount, $"إلغاء خصم مبيعات مرتجع - طلب #{order.OrderNumber}"));
        }

        if (totalVatReturn > 0)
        {
            string vatAcct = !string.IsNullOrEmpty(store?.StoreVatAccountId)
                ? $"ID:{store.StoreVatAccountId}"
                : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.VatOutput, mapDict)}";
            lines.Add((vatAcct, totalVatReturn, 0, _t.Get("Accounting.PartialReturnTaxCancelDesc", order.OrderNumber)));
        }

        decimal totalRefundValue = totalNetReturn + totalVatReturn; // Excludes shipping for partial returns

        if (chargeReturnShipping && returnShippingFee > 0)
        {
            lines.Add((receivablesAcct, returnShippingFee, 0, $"رسوم شحن إرجاع جزئي - طلب #{order.OrderNumber}"));
            lines.Add((deliveryRevAcct, 0, returnShippingFee, $"إيراد شحن إرجاع جزئي - طلب #{order.OrderNumber}"));
        }


        // ✅ ROBUST MULTI-RETURN DEBT LOGIC:
        // We calculate how much of the original debt is still "remaining" after previous returns.
        // This prevents the system from "forgetting" previous debt reductions and over-crediting the customer.
        int receivablesAcctId = int.Parse(receivablesAcct.Replace("ID:", ""));
        decimal alreadySettledDebt = await _db.JournalLines
            .Where(l => l.JournalEntry.OrderId == order.Id 
                     && l.JournalEntry.Type == JournalEntryType.SalesReturn 
                     && l.AccountId == receivablesAcctId)
            .SumAsync(l => l.Credit);

        decimal originalDebt = Math.Round(order.TotalAmount - order.PaidAmount, 2);
        decimal currentRemainingDebt = Math.Max(0, originalDebt - alreadySettledDebt);

        decimal amountToCustomerCredit;
        decimal amountToCashRefund;

        if (refundToStoreCredit)
        {
            amountToCustomerCredit = refundAmount;
            amountToCashRefund = 0;
        }
        else
        {
            decimal amountToCustomerCreditRaw = Math.Min(currentRemainingDebt, refundAmount);
            amountToCustomerCredit = amountToCustomerCreditRaw;
            amountToCashRefund = Math.Round(refundAmount - amountToCustomerCreditRaw, 2);
        }

        if (amountToCashRefund > 0)
        {
            string cashId;
            if (order.PaymentMethod == PaymentMethod.CustomerBalance)
            {
                cashId = receivablesAcct;
            }
            else
            {
                cashId = refundAccountId.HasValue
                    ? $"ID:{refundAccountId.Value}"
                    : await _core.GetMappedCashAccountAsync(order.PaymentMethod, order.Source, mapDict);
            }
            lines.Add((cashId, 0, amountToCashRefund, _t.Get("Accounting.PartialReturnCashRefundDesc", _core.GetMethodLabel(order.PaymentMethod), order.OrderNumber)));
        }

        if (amountToCustomerCredit > 0)
        {
            lines.Add((receivablesAcct, 0, amountToCustomerCredit, _t.Get("Accounting.PartialReturnDebtReductionDesc", order.OrderNumber)));
        }

        if (totalCostReturn > 0)
        {
            lines.Add((inventoryAcct, totalCostReturn, 0,              _t.Get("Accounting.PartialInventoryInDesc")));
            lines.Add((cogsAcct,      0,               totalCostReturn, _t.Get("Accounting.PartialCogsReductionDesc")));
        }

        await _core.PostEntryAsync(
            type:        JournalEntryType.SalesReturn,
            reference:   reference,
            description: _t.Get("Accounting.PartialReturnUnifiedMainDesc", order.OrderNumber, returnedItems.Count),
            date:        entryDate,
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId,
            source:      order.Source,
            createdAt:   entryDate
        );
    }

    public async Task PostDirectSalesReturnAsync(DirectReturnDto dto, string returnNumber, decimal totalCost, DateTime? overrideDate = null)
    {
        var mapDict = await _core.GetSafeSystemMappingsAsync();
        var store   = await _db.StoreInfo.FirstOrDefaultAsync(s => s.StoreConfigId == 1);

        string salesReturnAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.SalesReturn, mapDict)}";
        string inventoryAcct   = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Inventory,   mapDict)}";
        string cogsAcct        = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.COGS,        mapDict)}";

        string receivablesAcct;
        if (dto.CustomerId.HasValue)
        {
            var customer = await _db.Customers.FindAsync(dto.CustomerId.Value);
            if (customer?.MainAccountId != null)
                receivablesAcct = $"ID:{customer.MainAccountId}";
            else
                receivablesAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Customer, mapDict)}";
        }
        else
        {
            receivablesAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Customer, mapDict)}";
        }

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        decimal totalGrossAmount = 0;
        decimal totalVatAmount   = 0;

        foreach (var item in dto.Items)
        {
            var itemTotal = item.UnitPrice * item.Quantity;
            totalGrossAmount += itemTotal;

            if (item.HasTax)
            {
                var rate = (item.VatRate ?? store?.VatRatePercent ?? 0) / 100m;
                var net = Math.Round(itemTotal / (1 + rate), 2);
                totalVatAmount += (itemTotal - net);
            }
        }

        decimal totalNetReturn = totalGrossAmount - totalVatAmount;

        lines.Add((salesReturnAcct, totalNetReturn, 0, _t.Get("Accounting.DirectReturnNetDesc", returnNumber)));
        if (totalVatAmount > 0)
        {
            string vatAcct = !string.IsNullOrEmpty(store?.StoreVatAccountId)
                ? $"ID:{store.StoreVatAccountId}"
                : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.VatOutput, mapDict)}";
            lines.Add((vatAcct, totalVatAmount, 0, _t.Get("Accounting.DirectReturnTaxDesc", returnNumber)));
        }

        if ((dto.RefundMethod == PaymentMethod.Credit || dto.RefundMethod == PaymentMethod.CustomerBalance) && dto.CustomerId.HasValue)
        {
            lines.Add((receivablesAcct, 0, totalGrossAmount, _t.Get("Accounting.DirectReturnCreditDesc", dto.CustomerName ?? dto.CustomerId.Value.ToString())));
        }
        else
        {
            string cashId = dto.RefundAccountId.HasValue
                ? $"ID:{dto.RefundAccountId.Value}"
                : await _core.GetMappedCashAccountAsync(dto.RefundMethod, OrderSource.POS, mapDict);

            string methodLabel = _core.GetMethodLabel(dto.RefundMethod);
            lines.Add((cashId, 0, totalGrossAmount, _t.Get("Accounting.DirectReturnCashDesc", methodLabel, returnNumber)));
        }

        if (totalCost > 0)
        {
            lines.Add((inventoryAcct, totalCost, 0,         _t.Get("Accounting.DirectInventoryInDesc")));
            lines.Add((cogsAcct,      0,         totalCost, _t.Get("Accounting.DirectCogsReductionDesc")));
        }

        var entryDate = overrideDate ?? TimeHelper.GetEgyptTime();

        await _core.PostEntryAsync(
            type:        JournalEntryType.SalesReturn,
            reference:   returnNumber,
            description: _t.Get("Accounting.DirectReturnMainDesc", returnNumber, dto.CustomerName ?? ""),
            date:        entryDate,
            lines:       lines,
            customerId:  dto.CustomerId,
            source:      OrderSource.POS,
            createdAt:   entryDate
        );
    }

    public async Task PostCostPriceAdjustmentAsync(Order order, decimal originalTotalAmount, decimal originalVatAmount, string refundMethod)
    {
        if (order.Customer == null && order.CustomerId > 0)
        {
            var found = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CustomerId);
            if (found != null) order.Customer = found;
        }

        var mapDict = await _core.GetSafeSystemMappingsAsync();

        string salesReturnAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.SalesReturn,    mapDict)}";
        string receivablesAcct = order.Customer?.MainAccountId != null
            ? $"ID:{order.Customer.MainAccountId}"
            : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Customer, mapDict)}";

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        decimal difference = originalTotalAmount - order.TotalAmount;
        decimal vatDiff = originalVatAmount - order.TotalVatAmount;
        decimal netDiff = difference - vatDiff;

        lines.Add((salesReturnAcct, netDiff, 0, $"تخفيض المبيعات لتعديل التكلفة - فاتورة {order.OrderNumber}"));

        if (vatDiff > 0)
        {
            string vatAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.VatOutput, mapDict)}";
            lines.Add((vatAcct, vatDiff, 0, $"تخفيض ضريبة المبيعات لتعديل التكلفة - فاتورة {order.OrderNumber}"));
        }

        if (refundMethod == "cash")
        {
            decimal unpaidDebt = Math.Max(0, originalTotalAmount - order.PaidAmount);
            decimal amountToReceivables = Math.Min(unpaidDebt, difference);
            decimal amountToCash = difference - amountToReceivables;

            if (amountToReceivables > 0)
            {
                lines.Add((receivablesAcct, 0, amountToReceivables, $"تخفيض مديونية العميل لتعديل التكلفة - فاتورة {order.OrderNumber}"));
            }

            if (amountToCash > 0)
            {
                string cashId;
                if (order.PaymentMethod == PaymentMethod.CustomerBalance)
                {
                    cashId = receivablesAcct;
                }
                else
                {
                    cashId = await _core.GetMappedCashAccountAsync(order.PaymentMethod, order.Source, mapDict);
                }
                lines.Add((cashId, 0, amountToCash, $"استرداد لفرق التكلفة - فاتورة {order.OrderNumber}"));
            }
        }
        else // refundMethod == "credit"
        {
            lines.Add((receivablesAcct, 0, difference, $"إضافة فرق تعديل التكلفة لرصيد الحساب - فاتورة {order.OrderNumber}"));
        }

        var suffix = TimeHelper.GetEgyptTime().Ticks.ToString().Substring(10);
        var reference = $"{order.OrderNumber}-CST-{suffix}";

        await _core.PostEntryAsync(
            type:        JournalEntryType.SalesReturn,
            reference:   reference,
            description: $"تعديل الفاتورة رقم {order.OrderNumber} لسعر التكلفة",
            date:        TimeHelper.GetEgyptTime(),
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId,
            source:      order.Source
        );
    }

    /// <summary>
    /// قيد المرتجع (لشركة الشحن) - يطابق الصورة 8
    /// </summary>
    public async Task PostCourierReturnShippingFeeAsync(Order order, int? returnRequestId = null)
    {
        if (!order.ShippingCompanyId.HasValue || order.DeliveryFee <= 0)
            return;

        var reference = returnRequestId.HasValue ? $"{order.OrderNumber}-SHP-RTN-{returnRequestId}" : $"{order.OrderNumber}-SHP-RTN";
        if (await _core.EntryExistsAsync(JournalEntryType.Manual, reference))
            return;

        if (order.ShippingCompany == null && order.ShippingCompanyId.HasValue)
        {
            order.ShippingCompany = await _db.ShippingCompanies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == order.ShippingCompanyId.Value);
        }

        if (order.ShippingCompany?.AccountId == null)
            return;

        if (order.Customer == null && order.CustomerId > 0)
        {
            order.Customer = await _db.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == order.CustomerId) ?? new Customer();
        }

        var mapDict = await _core.GetSafeSystemMappingsAsync();
        string customerAcct = order.Customer?.MainAccountId != null
            ? $"ID:{order.Customer.MainAccountId}"
            : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Customer, mapDict)}";
            
        var store = await _db.StoreInfo.FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        string deliveryRevAcct = !string.IsNullOrEmpty(store?.DeliveryRevenueAccountId)
            ? $"ID:{store.DeliveryRevenueAccountId}"
            : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.DeliveryRevenue, mapDict)}";
            
        string deliveryExpAcct = !string.IsNullOrEmpty(store?.DeliveryAccountId)
            ? $"ID:{store.DeliveryAccountId}"
            : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.DeliveryExpense, mapDict)}";

        string courierAcct = $"ID:{order.ShippingCompany.AccountId}";
        
        decimal shippingFee = order.DeliveryFee;
        // In return cases, we might charge the courier's actual cost, but usually the system uses ActualDeliveryCost, fallback to DeliveryFee
        decimal courierCost = order.ActualDeliveryCost > 0 ? order.ActualDeliveryCost : order.DeliveryFee; 
        
        var postingDate = TimeHelper.GetEgyptTime();

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>
        {
            // 1. مدين: إيراد خدمة توصيل / دائن: عميل (بإجمالي قيمة الشحن لإلغاء الإيراد الوهمي وإسقاطه من العميل)
            (deliveryRevAcct, shippingFee, 0,           $"إلغاء إيراد الشحن لعدم التسليم - فاتورة #{order.OrderNumber}"),
            (customerAcct,    0,           shippingFee, $"إسقاط مديونية الشحن لعدم التسليم - فاتورة #{order.OrderNumber}"),
            
            // 2. مدين: مصروف شحن وتوصيل / دائن: شركة الشحن (قيمة تكلفة الشحن المستحقة للشركة نظير المحاولة)
            (deliveryExpAcct, courierCost, 0,           $"مصاريف شحن مرتجع - {order.ShippingCompany.NameAr} | فاتورة #{order.OrderNumber}"),
            (courierAcct,     0,           courierCost, $"استحقاق مصاريف شحن لشركة - {order.ShippingCompany.NameAr} | فاتورة #{order.OrderNumber}")
        };

        await _core.PostEntryAsync(
            type:        JournalEntryType.Manual,
            reference:   reference,
            description: $"قيد شحن مرتجع — {order.ShippingCompany.NameAr} | {(returnRequestId.HasValue ? $"طلب #{returnRequestId} | " : "")}فاتورة #{order.OrderNumber}",
            date:        TimeHelper.GetEgyptBusinessDayDate(postingDate),
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId,
            source:      order.Source,
            createdAt:   postingDate
        );
    }

    /// <summary>

    /// تم إيقاف هذا القيد ليتم نقله إلى شاشة تسويات الشحن بناء على طلب المحاسب

    /// </summary>

    public Task PostSuccessfulDeliveryAccountingAsync(Order order)

    {

        return Task.CompletedTask;

    }

    public async Task PostWarehouseReceiptFromCourierAsync(Order order, int returnRequestId)
    {
        // ── 1. تأكد من تحميل العناصر والمنتجات ──
        if (order.Items == null || !order.Items.Any())
            try { await _db.Entry(order).Collection(o => o.Items).LoadAsync(); } catch { }

        if (order.Items != null && order.Items.Any(i => i.Product == null))
        {
            try
            {
                var productIds = order.Items.Where(i => i.ProductId.HasValue)
                                            .Select(i => i.ProductId!.Value).Distinct().ToList();
                var products = await _db.Products.AsNoTracking()
                                        .Where(p => productIds.Contains(p.Id)).ToListAsync();
                foreach (var item in order.Items)
                    item.Product ??= products.FirstOrDefault(p => p.Id == item.ProductId);
            }
            catch { }
        }

        // ── 2. حساب تكلفة المخزون الراجع ──
        decimal totalCostReturn = 0;
        if (order.Items != null)
        {
            foreach (var item in order.Items)
            {
                totalCostReturn += (item.Product?.CostPrice ?? 0) * item.Quantity;
            }
        }

        if (totalCostReturn <= 0) return; // No cost to transfer

        // ── 3. جلب الحسابات ──
        var mapDict = await _core.GetSafeSystemMappingsAsync();
        string inventoryAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Inventory, mapDict)}";
        
        if (!mapDict.TryGetValue(MK.CourierInventory.ToLower(), out var courierInvId) || !courierInvId.HasValue)
        {
            _logger.LogWarning("[Accounting] CourierInventory account not mapped, cannot post warehouse receipt transfer for order #{OrderNumber}", order.OrderNumber);
            return;
        }
        string courierInventoryAcct = $"ID:{courierInvId.Value}";

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        // [ مدين ] المخزن الرئيسي (Inventory)
        lines.Add((inventoryAcct, totalCostReturn, 0,
            $"إثبات استلام مخزون من شركة الشحن - طلب #{order.OrderNumber}"));

        // [ دائن ] مخزن شركة الشحن (Courier Inventory)
        lines.Add((courierInventoryAcct, 0, totalCostReturn,
            $"صرف مخزون من شركة الشحن للمخزن الرئيسي - طلب #{order.OrderNumber}"));

        // ── 4. تسجيل القيد ──
        await _core.PostEntryAsync(
            type:        JournalEntryType.Manual,
            reference:   $"{order.OrderNumber}-WH-RTN-{returnRequestId}",
            description: $"قيد استلام مخزون من شركة الشحن لطلب #{order.OrderNumber} - استرجاع #{returnRequestId}",
            date:        TimeHelper.GetEgyptBusinessDayDate(TimeHelper.GetEgyptTime()),
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId,
            source:      order.Source,
            createdAt:   TimeHelper.GetEgyptTime()
        );

        _logger.LogInformation("[Accounting] Posted Warehouse Receipt from Courier entry for Order #{OrderNumber}", order.OrderNumber);
    }
}
