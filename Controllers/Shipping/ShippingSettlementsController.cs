using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using Sportive.API.Services;

namespace Sportive.API.Controllers.Shipping;

[ApiController]
[Route("api/shipping-settlements")]
[Authorize(Policy = "AdminOnly")]
public class ShippingSettlementsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccountingCoreService _accountingCore;

    public ShippingSettlementsController(AppDbContext db, AccountingCoreService accountingCore)
    {
        _db = db;
        _accountingCore = accountingCore;
    }

    [HttpGet("pending/{companyId}")]
    public async Task<IActionResult> GetPendingSettlements(int companyId)
    {
        var orders = await _db.Orders
            .Include(o => o.Customer)
            .Where(o => o.ShippingCompanyId == companyId && 
                        o.Status == OrderStatus.Delivered &&
                        o.PaymentMethod == PaymentMethod.Cash && 
                        o.IsSettledWithCourier == false &&
                        o.Source != OrderSource.POS)
            .OrderBy(o => o.ActualDeliveryDate ?? o.UpdatedAt)
            .Select(o => new {
                o.Id,
                o.OrderNumber,
                CustomerName = o.Customer.FullName,
                o.TotalAmount,
                DeliveryDate = o.ActualDeliveryDate ?? o.UpdatedAt,
                o.ShippingTrackingNumber,
                o.BostaTrackingNumber,
                ActualDeliveryCost = o.ActualDeliveryCost == 0 ? o.DeliveryFee : o.ActualDeliveryCost
            })
            .ToListAsync();

        return Ok(orders);
    }

    [HttpPost("settle")]
    public async Task<IActionResult> SettleOrders([FromBody] SettleShippingRequest request)
    {
        var orderIds = request.Orders?.Select(o => o.OrderId).ToList() ?? request.OrderIds;
        if (orderIds == null || !orderIds.Any())
            return BadRequest("يجب تحديد الطلبات المراد تسويتها.");

        var company = await _db.ShippingCompanies.FindAsync(request.ShippingCompanyId);
        if (company == null || company.AccountId == null)
            return BadRequest("شركة الشحن غير موجودة أو غير مربوطة بحساب في الدليل المحاسبي.");

        var orders = await _db.Orders
            .Where(o => orderIds.Contains(o.Id) && 
                        o.ShippingCompanyId == request.ShippingCompanyId &&
                        o.IsSettledWithCourier == false)
            .ToListAsync();

        if (!orders.Any())
            return BadRequest("لا توجد طلبات متاحة للتسوية أو تم تسويتها مسبقاً.");

        // Get Store Settings for Delivery Expense Account
        var storeSettings = await _db.StoreInfo.AsNoTracking().FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        string deliveryExpenseAccount = storeSettings?.DeliveryAccountId ?? "511";

        // Get Cash Account from Mapping
        var mapDict = await _accountingCore.GetSafeSystemMappingsAsync();
        var cashAccountCode = await _accountingCore.GetMappedCashAccountAsync(request.Method, OrderSource.Website, mapDict);

        // Update shipping costs and calculate totals
        foreach (var order in orders)
        {
            var reqOrder = request.Orders?.FirstOrDefault(o => o.OrderId == order.Id);
            if (reqOrder != null)
            {
                order.ActualDeliveryCost = reqOrder.ActualDeliveryCost;
            }
            order.IsSettledWithCourier = true;
            order.CourierSettlementDate = TimeHelper.GetEgyptTime();
        }

        decimal totalCollected = orders.Sum(o => o.TotalAmount);
        decimal totalShippingCost = orders.Sum(o => o.ActualDeliveryCost);
        decimal netAmount = totalCollected - totalShippingCost;

        // إنشاء قيد التسوية
        var reference = $"SETTLE-SHP-{company.Id}-{DateTime.Now:yyyyMMddHHmmss}";
        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        // 1. حساب البنك/الخزينة (صافي التحويل)
        if (netAmount > 0)
        {
            lines.Add((cashAccountCode, netAmount, 0, $"تسوية متحصلات من شركة شحن: {company.NameAr} - لعدد {orders.Count} طلب"));
        }
        else if (netAmount < 0)
        {
            lines.Add((cashAccountCode, 0, Math.Abs(netAmount), $"سداد عجز وتسوية شركة شحن: {company.NameAr} - لعدد {orders.Count} طلب"));
        }

        // 2. حساب مصاريف الشحن (العمولة المدفوعة للشركة)
        if (totalShippingCost > 0)
        {
            lines.Add((deliveryExpenseAccount, totalShippingCost, 0, $"مصاريف شحن وتوصيل لشركة {company.NameAr} (تسوية {orders.Count} طلب)"));
        }

        // 3. حساب شركة الشحن (دائن لتقليل مديونيتهم بإجمالي المبالغ المحصلة)
        if (totalCollected > 0)
        {
            lines.Add(($"ID:{company.AccountId}", 0, totalCollected, $"تسديد متحصلات طلبات مجمعة"));
        }

        // Check if unbalanced due to only negative netAmount (unlikely if totalCollected > 0)
        if (lines.Any())
        {
            // تسجيل القيد
            await _accountingCore.PostEntryAsync(
                type: JournalEntryType.ReceiptVoucher,
                reference: reference,
                description: $"تسوية مدفوعات شركة الشحن: {company.NameAr}",
                date: TimeHelper.GetEgyptBusinessDayDate(DateTime.UtcNow),
                lines: lines,
                source: OrderSource.Website
            );
        }

        await _db.SaveChangesAsync();

        return Ok(new { success = true, totalSettled = netAmount, count = orders.Count });
    }

    [HttpPost("sync-bosta-prices")]
    public async Task<IActionResult> SyncBostaPrices([FromBody] SyncBostaPricesRequest request)
    {
        if (request == null || request.OrderIds == null || !request.OrderIds.Any())
            return BadRequest("يجب تحديد الطلبات.");

        var storeSettings = await _db.StoreInfo.AsNoTracking().FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        if (storeSettings == null || string.IsNullOrEmpty(storeSettings.BostaApiKey))
            return BadRequest("Bosta API Key is not configured.");

        string baseUrl = storeSettings.BostaUseSandbox ? "https://stg-api.bosta.co" : "https://api.bosta.co";
        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", storeSettings.BostaApiKey);

        var orders = await _db.Orders
            .Where(o => request.OrderIds.Contains(o.Id) && !string.IsNullOrEmpty(o.BostaDeliveryId))
            .ToListAsync();

        if (!orders.Any())
            return BadRequest("لا توجد طلبات برقم تتبع بوسطة في القائمة المحددة.");

        int successCount = 0;
        List<string> debugLogs = new();

        foreach (var order in orders)
        {
            try
            {
                var response = await client.GetAsync($"{baseUrl}/api/v2/deliveries/{order.BostaDeliveryId}");
                var jsonStr = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
                    var root = doc.RootElement;
                    
                    decimal foundCost = 0;
                    
                    var targetElement = root;
                    if (root.TryGetProperty("data", out var dataObj) && dataObj.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        targetElement = dataObj;
                    }

                    if (targetElement.TryGetProperty("pricing", out var pricingObj))
                    {
                        decimal bostaRevenue = 0, shippingCost = 0, vat = 0, insurance = 0;

                        if (pricingObj.TryGetProperty("bostaRevenue", out var revProp) && revProp.TryGetDecimal(out var rev)) bostaRevenue = rev;
                        else if (pricingObj.TryGetProperty("bostaDue", out var dueProp) && dueProp.TryGetDecimal(out var due)) bostaRevenue = due;
                        else if (pricingObj.TryGetProperty("netRevenue", out var netProp) && netProp.TryGetDecimal(out var net)) bostaRevenue = net;

                        if (pricingObj.TryGetProperty("shippingCost", out var costProp) && costProp.TryGetDecimal(out var cost)) shippingCost = cost;
                        else if (pricingObj.TryGetProperty("shippingPrice", out var costProp2) && costProp2.TryGetDecimal(out var cost2)) shippingCost = cost2;

                        if (pricingObj.TryGetProperty("vat", out var vatProp) && vatProp.TryGetDecimal(out var v)) vat = v;
                        if (pricingObj.TryGetProperty("insurance", out var insProp) && insProp.TryGetDecimal(out var i)) insurance = i;

                        decimal calculatedDue = shippingCost + vat + insurance;
                        foundCost = bostaRevenue > 0 ? bostaRevenue : (calculatedDue > 0 ? calculatedDue : shippingCost);
                        
                        debugLogs.Add($"Order {order.Id} Pricing: {pricingObj.GetRawText()} | Found: {foundCost}");
                    }
                    else if (targetElement.TryGetProperty("price", out var priceProp) && priceProp.TryGetDecimal(out var priceObjVal))
                    {
                        foundCost = priceObjVal;
                        debugLogs.Add($"Order {order.Id} Price: {priceObjVal}");
                    }
                    else
                    {
                        debugLogs.Add($"Order {order.Id} Missing Pricing, keys: {string.Join(",", targetElement.EnumerateObject().Select(p => p.Name))}");
                    }

                    if (foundCost > 0)
                    {
                        order.ActualDeliveryCost = foundCost;
                        successCount++;
                    }
                }
                else
                {
                    debugLogs.Add($"Order {order.Id} Failed API Call: {response.StatusCode} - {jsonStr}");
                }
            }
            catch (Exception ex) 
            { 
                debugLogs.Add($"Order {order.Id} Exception: {ex.Message}");
            }
        }

        if (successCount > 0)
            await _db.SaveChangesAsync();

        return Ok(new { success = true, syncedCount = successCount, totalRequested = orders.Count, debugLogs });
    }
}

public class SyncBostaPricesRequest
{
    public List<int> OrderIds { get; set; } = new();
}

public class SettleShippingRequest
{
    public int ShippingCompanyId { get; set; }
    public List<int> OrderIds { get; set; } = new();
    public List<SettleOrderDto> Orders { get; set; } = new();
    public PaymentMethod Method { get; set; } = PaymentMethod.Bank;
}

public class SettleOrderDto
{
    public int OrderId { get; set; }
    public decimal ActualDeliveryCost { get; set; }
}
