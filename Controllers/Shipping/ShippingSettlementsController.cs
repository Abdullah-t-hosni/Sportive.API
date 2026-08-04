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
                string id = order.BostaDeliveryId;
                string trk = order.BostaTrackingNumber;
                string[] possibleEndpoints = {
                    $"/api/v2/deliveries/business/{id}",
                    $"/api/v2/deliveries/business/{trk}",
                    $"/api/v0/deliveries/{id}",
                    $"/api/v0/deliveries/{trk}"
                };

                bool foundValidResponse = false;

                foreach (var endpoint in possibleEndpoints)
                {
                    if (foundValidResponse || string.IsNullOrEmpty(endpoint.Split('/').Last())) continue;

                    var response = await client.GetAsync($"{baseUrl}{endpoint}");
                    var jsonStr = await response.Content.ReadAsStringAsync();
                    
                    if (response.IsSuccessStatusCode && jsonStr.TrimStart().StartsWith("{"))
                    {
                        foundValidResponse = true;
                        using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
                        var root = doc.RootElement;
                        
                        decimal foundCost = 0;
                        
                        var targetElement = root;
                        if (root.TryGetProperty("message", out var msgObj) && msgObj.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            targetElement = msgObj; // Some Bosta v0 endpoints return it in "message"
                        }
                        else if (root.TryGetProperty("data", out var dataObj))
                        {
                            if (dataObj.ValueKind == System.Text.Json.JsonValueKind.Array && dataObj.GetArrayLength() > 0)
                                targetElement = dataObj[0];
                            else if (dataObj.ValueKind == System.Text.Json.JsonValueKind.Object)
                                targetElement = dataObj;
                        }


                        // Bosta sometimes hides the full pricing inside the log array or provides 'shipmentFees' (without VAT).
                        if (targetElement.TryGetProperty("log", out var logArray) && logArray.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            // Search backwards to find the latest pricing update
                            for (int j = logArray.GetArrayLength() - 1; j >= 0; j--)
                            {
                                var logItem = logArray[j];
                                if (logItem.TryGetProperty("actionsList", out var actionsList) && 
                                    actionsList.TryGetProperty("pricing", out var logPricing) && 
                                    logPricing.TryGetProperty("after", out var pricingAfter) &&
                                    pricingAfter.TryGetProperty("priceAfterVat", out var priceAfterVatProp) && 
                                    priceAfterVatProp.TryGetDecimal(out var logPrice))
                                {
                                    // تقريب لأقرب رقم عشري واحد ليتطابق مع واجهة بوسطة 100%
                                    foundCost = Math.Round(logPrice, 1, MidpointRounding.AwayFromZero);
                                    break;
                                }
                            }
                        }

                        if (foundCost == 0)
                        {
                            if (targetElement.TryGetProperty("shipmentFees", out var shipFeesProp) && shipFeesProp.TryGetDecimal(out var shipFees))
                            {
                                // Bosta shipmentFees usually excludes 14% VAT
                                foundCost = Math.Round(shipFees * 1.14m, 1, MidpointRounding.AwayFromZero);
                            }
                        }

                        if (foundCost == 0 && targetElement.TryGetProperty("price", out var priceProp) && priceProp.TryGetDecimal(out var priceObjVal))
                        {
                            foundCost = Math.Round(priceObjVal, 1, MidpointRounding.AwayFromZero);
                        }

                        if (foundCost == 0)
                        {
                            debugLogs.Add($"Order {order.Id} RAW: {targetElement.GetRawText()}");
                        }

                        if (foundCost > 0)
                        {
                            order.ActualDeliveryCost = foundCost;
                            successCount++;
                        }
                    }
                    else
                    {
                        debugLogs.Add($"Order {order.Id} Failed API Call ({endpoint}): {response.StatusCode} - {jsonStr}");
                    }
                } // End foreach endpoint
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
