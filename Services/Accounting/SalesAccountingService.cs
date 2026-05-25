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
    // ══════════════════════════════════════════════════════
    public async Task PostSalesOrderAsync(Order order)
    {
        if (order.Customer == null && order.CustomerId > 0)
        {
            order.Customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CustomerId);
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
            var city = order.DeliveryAddress.City.Trim().ToLower();
            var zones = await _db.ShippingZones.AsNoTracking().ToListAsync();
            var matchedZone = zones.FirstOrDefault(z => z.IsActive && z.Governorates.ToLower().Split(',').Any(g => g.Trim() == city));
            
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

        decimal handledPaidAmt = 0;
        var payments = order.Payments?.Where(p => p.Amount > 0 && p.Method != PaymentMethod.Credit).ToList()
                    ?? new List<OrderPayment>();

        if (payments.Any())
        {
            foreach (var p in payments)
            {
                var cashAcct = await _core.GetMappedCashAccountAsync(p.Method, order.Source, mapDict);
                string methodLabel = _core.GetMethodLabel(p.Method);
                lines.Add((cashAcct, p.Amount, 0, _t.Get("Accounting.CollectionDesc", methodLabel, order.OrderNumber)));
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
                    lines.Add((cashAcct, v, 0, _t.Get("Accounting.CollectionDesc", _core.GetMethodLabel(m), order.OrderNumber)));
                    handledPaidAmt += v;
                }
            }
            else if (order.PaymentMethod != PaymentMethod.Credit && order.PaidAmount > 0)
            {
                var cashAcct = await _core.GetMappedCashAccountAsync(order.PaymentMethod, order.Source, mapDict);
                decimal payAmt = order.PaidAmount;
                lines.Add((cashAcct, payAmt, 0, _t.Get("Accounting.CollectionDesc", _core.GetMethodLabel(order.PaymentMethod), order.OrderNumber)));
                handledPaidAmt = payAmt;
            }
        }

        // ⚠️ STRICT VALIDATION: No silent adjustments or magic fixes.
        if (Math.Abs(handledPaidAmt - order.PaidAmount) > 0.01m)
        {
            throw new InvalidOperationException(_t.Get("Accounting.PaymentMismatchError", order.PaidAmount, handledPaidAmt));
        }

        // Remaining debt → Receivables
        var remainingDebt = Math.Round(order.TotalAmount - handledPaidAmt, 2);

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
        var totalCost = order.Items?.Sum(i => (i.Product?.CostPrice ?? 0) * i.Quantity) ?? 0;
        if (totalCost > 0)
        {
            lines.Add((cogsAcct,      totalCost, 0,         _t.Get("Accounting.CogsDesc", order.OrderNumber)));
            lines.Add((inventoryAcct, 0,         totalCost, _t.Get("Accounting.InventoryOutDesc", order.OrderNumber)));
        }

        var entry = await _core.PostEntryAsync(
            type:        JournalEntryType.SalesInvoice,
            reference:   order.OrderNumber,
            description: _t.Get("Accounting.SalesEntryMainDesc", order.OrderNumber, order.Customer?.FullName ?? ""),
            date:        TimeHelper.GetEgyptBusinessDayDate(order.CreatedAt),
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId,
            source:      order.Source,
            employeeId:  employeeId,
            createdAt:   order.CreatedAt
        );
    }

    public async Task PostSalesReturnAsync(Order order, int? refundAccountId = null)
    {
        if (order.Customer == null && order.CustomerId > 0)
        {
            order.Customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CustomerId);
        }

        var mapDict = await _core.GetSafeSystemMappingsAsync();
        var store   = await _db.StoreInfo.FirstOrDefaultAsync(s => s.StoreConfigId == 1);

        string salesReturnAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.SalesReturn, mapDict)}";
        string inventoryAcct   = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Inventory,   mapDict)}";
        string cogsAcct        = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.COGS,        mapDict)}";

        string receivablesAcct;
        if (order.Customer?.MainAccountId != null)
            receivablesAcct = $"ID:{order.Customer.MainAccountId}";
        else
            receivablesAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Customer, mapDict)}";

        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        var totalVatAmount = order.TotalVatAmount;
        var netReturnPrice = order.TotalAmount - totalVatAmount;

        lines.Add((salesReturnAcct, netReturnPrice, 0, _t.Get("Accounting.SalesReturnDesc", order.OrderNumber)));
        if (totalVatAmount > 0)
        {
            string vatAcct = !string.IsNullOrEmpty(store?.StoreVatAccountId)
                ? $"ID:{store.StoreVatAccountId}"
                : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.VatOutput, mapDict)}";
            lines.Add((vatAcct, totalVatAmount, 0, _t.Get("Accounting.SalesReturnTaxDesc", order.OrderNumber)));
        }

        // ✅ ROBUST MULTI-RETURN DEBT LOGIC:
        // Even in full returns, we must check if partial returns already settled some of the debt.
        int receivablesAcctId = int.Parse(receivablesAcct.Replace("ID:", ""));
        decimal alreadySettledDebt = await _db.JournalLines
            .Where(l => l.JournalEntry.OrderId == order.Id 
                     && l.JournalEntry.Type == JournalEntryType.SalesReturn 
                     && l.AccountId == receivablesAcctId)
            .SumAsync(l => l.Credit);

        decimal originalDebt = Math.Round(order.TotalAmount - order.PaidAmount, 2);
        decimal currentRemainingDebt = Math.Max(0, originalDebt - alreadySettledDebt);

        decimal totalRefundValue = order.TotalAmount; // In full return, we refund everything
        decimal creditRefundAmount = Math.Min(currentRemainingDebt, totalRefundValue);
        decimal cashRefundAmount   = Math.Round(totalRefundValue - creditRefundAmount, 2);

        if (cashRefundAmount > 0)
        {
            string cashId = refundAccountId.HasValue
                ? $"ID:{refundAccountId.Value}"
                : await _core.GetMappedCashAccountAsync(order.PaymentMethod, order.Source, mapDict);

            string methodLabel = _core.GetMethodLabel(order.PaymentMethod);
            lines.Add((cashId, 0, cashRefundAmount, _t.Get("Accounting.SalesReturnRefundDesc", methodLabel, order.OrderNumber)));
        }

        if (creditRefundAmount > 0)
        {
            lines.Add((receivablesAcct, 0, creditRefundAmount, _t.Get("Accounting.SalesReturnDebtReductionDesc", order.Customer?.FullName ?? order.OrderNumber)));
        }

        var totalCost = order.Items?.Sum(i => (i.Product?.CostPrice ?? 0) * i.Quantity) ?? 0;
        if (totalCost > 0)
        {
            lines.Add((inventoryAcct, totalCost, 0,         _t.Get("Accounting.InventoryInDesc")));
            lines.Add((cogsAcct,      0,         totalCost, _t.Get("Accounting.CogsReductionDesc")));
        }

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

    public async Task PostPartialSalesReturnAsync(Order order, List<OrderItem> returnedItems, decimal refundAmount, int? refundAccountId = null, bool refundToStoreCredit = false)
    {
        if (order.Customer == null && order.CustomerId > 0)
        {
            order.Customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CustomerId);
        }

        var suffix    = TimeHelper.GetEgyptTime().Ticks.ToString().Substring(10);
        var reference = $"{order.OrderNumber}-PRT-{suffix}";

        var mapDict = await _core.GetSafeSystemMappingsAsync();

        string salesReturnAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.SalesReturn,    mapDict)}";
        string salesDiscAcct   = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.SalesDiscount,  mapDict)}";
        
        string receivablesAcct;
        if (order.Customer?.MainAccountId != null)
            receivablesAcct = $"ID:{order.Customer.MainAccountId}";
        else
            receivablesAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Customer, mapDict)}";

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

        lines.Add((salesReturnAcct, totalNetReturn, 0, _t.Get("Accounting.PartialReturnNetDesc", order.OrderNumber)));
        if (totalVatReturn > 0)
        {
            string vatAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.VatOutput, mapDict)}";
            lines.Add((vatAcct, totalVatReturn, 0, _t.Get("Accounting.PartialReturnTaxCancelDesc", order.OrderNumber)));
        }

        decimal discountReversal = (totalNetReturn + totalVatReturn) - refundAmount;
        if (discountReversal > 0)
            lines.Add((salesDiscAcct, 0, discountReversal, _t.Get("Accounting.PartialReturnDiscountBalDesc", order.OrderNumber)));
        else if (discountReversal < 0)
            lines.Add((salesDiscAcct, Math.Abs(discountReversal), 0, _t.Get("Accounting.PartialReturnDiffAdjDesc", order.OrderNumber)));

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
            string cashId = refundAccountId.HasValue
                ? $"ID:{refundAccountId.Value}"
                : await _core.GetMappedCashAccountAsync(order.PaymentMethod, order.Source, mapDict);
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
            date:        TimeHelper.GetEgyptTime(),
            lines:       lines,
            orderId:     order.Id,
            customerId:  order.CustomerId,
            source:      order.Source
        );
    }

    public async Task PostDirectSalesReturnAsync(DirectReturnDto dto, string returnNumber, decimal totalCost)
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

        if (dto.RefundMethod == PaymentMethod.Credit && dto.CustomerId.HasValue)
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

        await _core.PostEntryAsync(
            type:        JournalEntryType.SalesReturn,
            reference:   returnNumber,
            description: _t.Get("Accounting.DirectReturnMainDesc", returnNumber, dto.CustomerName ?? ""),
            date:        TimeHelper.GetEgyptTime(),
            lines:       lines,
            customerId:  dto.CustomerId,
            source:      OrderSource.POS
        );
    }

    public async Task PostCostPriceAdjustmentAsync(Order order, decimal originalTotalAmount, decimal originalVatAmount, string refundMethod)
    {
        if (order.Customer == null && order.CustomerId > 0)
        {
            order.Customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CustomerId);
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
                string cashId = await _core.GetMappedCashAccountAsync(order.PaymentMethod, order.Source, mapDict);
                lines.Add((cashId, 0, amountToCash, $"استرداد نقدي لفرق التكلفة - فاتورة {order.OrderNumber}"));
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
}

