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
    public async Task<IActionResult> GetPendingSettlements(int companyId, [FromQuery] string statusFilter = "pending")
    {
        try
        {
            return await FetchSettlementOrdersAsync(companyId, statusFilter);
        }
        catch (Exception ex) when (ex.Message.Contains("CourierSettlementReference"))
        {
            try
            {
                await _db.Database.ExecuteSqlRawAsync("ALTER TABLE `Orders` ADD COLUMN `CourierSettlementReference` longtext NULL;");
            }
            catch { }
            return await FetchSettlementOrdersAsync(companyId, statusFilter);
        }
    }

    private async Task<IActionResult> FetchSettlementOrdersAsync(int companyId, string statusFilter)
    {
        var validStatuses = new[] { OrderStatus.Delivered, OrderStatus.Returned, OrderStatus.PartiallyReturned };
        
        var q = _db.Orders
            .Include(o => o.Customer)
            .Where(o => o.ShippingCompanyId == companyId && 
                        validStatuses.Contains(o.Status) &&
                        o.Source != OrderSource.POS);

        if (statusFilter == "pending")
        {
            q = q.Where(o => o.IsSettledWithCourier == false);
        }
        else if (statusFilter == "settled")
        {
            q = q.Where(o => o.IsSettledWithCourier == true);
        }

        var orders = await q
            .OrderByDescending(o => o.CourierSettlementDate)
            .ThenByDescending(o => o.ActualDeliveryDate ?? o.UpdatedAt)
            .Select(o => new {
                o.Id,
                o.OrderNumber,
                CustomerName = o.Customer.FullName,
                o.TotalAmount,
                DeliveryDate = o.ActualDeliveryDate ?? o.UpdatedAt,
                CreatedAt = o.CreatedAt,
                o.ShippingTrackingNumber,
                o.BostaTrackingNumber,
                ActualDeliveryCost = o.ActualDeliveryCost == 0 ? o.DeliveryFee : o.ActualDeliveryCost,
                DeliveryFee = o.DeliveryFee,
                o.IsSettledWithCourier,
                o.CourierSettlementDate,
                o.CourierSettlementReference,
                Status = o.Status.ToString(),
                PaymentMethod = o.PaymentMethod.ToString()
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
        string? deliveryExpenseAccount = storeSettings?.DeliveryAccountId;

        if (string.IsNullOrEmpty(deliveryExpenseAccount) || deliveryExpenseAccount == "511")
        {
            var mapDictTemp = await _accountingCore.GetSafeSystemMappingsAsync();
            if (mapDictTemp.TryGetValue(Utils.MappingKeys.DeliveryExpense.ToLower(), out var devAccId) && devAccId.HasValue)
            {
                deliveryExpenseAccount = $"ID:{devAccId.Value}";
            }
            else
            {
                var delAcc = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "5220706" || a.NameAr.Contains("مصروف خدمة التوصيل") || a.NameAr.Contains("مصاريف شحن") || a.NameAr.Contains("مصروف شحن") || a.NameAr.Contains("مصاريف توصيل"));
                if (delAcc != null)
                {
                    deliveryExpenseAccount = delAcc.IsLeaf ? $"ID:{delAcc.Id}" : delAcc.Code;
                }
                else
                {
                    var adminExp = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "522" || a.Code == "52" || a.Code == "51");
                    var newDelAcc = new Account
                    {
                        Code = "5220706",
                        NameAr = "مصروف خدمة التوصيل",
                        NameEn = "Delivery Service Expense",
                        Type = AccountType.Expense,
                        Nature = AccountNature.Debit,
                        Level = 3,
                        ParentId = adminExp?.Id,
                        IsLeaf = true,
                        AllowPosting = true,
                        IsSystem = true,
                        CreatedAt = TimeHelper.GetEgyptTime()
                    };
                    _db.Accounts.Add(newDelAcc);
                    await _db.SaveChangesAsync();
                    deliveryExpenseAccount = $"ID:{newDelAcc.Id}";
                }
            }
        }

        // Get Cash Account from TargetAccountId or System Mapping (Website / Online Store Branch)
        string cashAccountCode;
        if (request.TargetAccountId.HasValue && request.TargetAccountId.Value > 0)
        {
            cashAccountCode = $"ID:{request.TargetAccountId.Value}";
        }
        else
        {
            var mapDict = await _accountingCore.GetSafeSystemMappingsAsync();
            cashAccountCode = await _accountingCore.GetMappedCashAccountAsync(request.Method, OrderSource.Website, mapDict);
        }

        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var collectionRef = $"SETTLE-COLLECTION-{company.Id}-{timestamp}";

        // Update shipping costs, set settlement flag and batch reference
        foreach (var order in orders)
        {
            var reqOrder = request.Orders?.FirstOrDefault(o => o.OrderId == order.Id);
            bool isExplicitlySet = false;

            if (reqOrder != null)
            {
                order.ActualDeliveryCost = reqOrder.ActualDeliveryCost;
                isExplicitlySet = true;
            }
            
            // 🔥 Fallback: Only if the frontend didn't send this order at all (which shouldn't happen, but just in case)
            if (!isExplicitlySet && order.ActualDeliveryCost == 0 && order.DeliveryFee > 0)
            {
                order.ActualDeliveryCost = order.DeliveryFee;
            }

            order.IsSettledWithCourier = true;
            order.CourierSettlementDate = TimeHelper.GetEgyptTime();
            order.CourierSettlementReference = collectionRef;
        }

        decimal totalCollected = 0;
        decimal totalShippingCost = 0;

        DateTime collectionDate = request.CollectionDate ?? TimeHelper.GetEgyptTime();
        DateTime invoiceDate = request.InvoiceDate ?? collectionDate;
        var currentSysTime = TimeHelper.GetEgyptTime();

        foreach (var order in orders)
        {
            var reqOrder = request.Orders?.FirstOrDefault(x => x.OrderId == order.Id);
            decimal collected = reqOrder?.CollectedAmount ?? order.TotalAmount;
            totalCollected += collected;
            totalShippingCost += reqOrder?.ActualDeliveryCost ?? order.ActualDeliveryCost;

            // 🔥 FIX: If this is an order where Bosta did NOT collect anything ("لم يحصل")
            // We MUST reverse the premature Debit to Bosta that was created in PostCourierReturnShippingFeeAsync!
            if (collected == 0)
            {
                var returnEntry = await _db.JournalEntries
                    .Include(e => e.Lines)
                    .FirstOrDefaultAsync(e => e.OrderId == order.Id && e.Reference != null && e.Reference.Contains("-SHP-RTN-") && !e.Reference.StartsWith("REV-"));
                    
                if (returnEntry != null)
                {
                    // Verify we haven't already reversed it
                    bool alreadyReversed = await _db.JournalEntries.AnyAsync(e => e.Reference == $"REV-{returnEntry.Reference}");
                    if (!alreadyReversed)
                    {
                        var reversalLines = returnEntry.Lines.Select(l => (
                            code: $"ID:{l.AccountId}",
                            debit: l.Credit,
                            credit: l.Debit,
                            desc: $"عكس قيد مصاريف شحن لعدم التحصيل - فاتورة #{order.OrderNumber}"
                        )).ToList();

                        var mapDict = await _accountingCore.GetSafeSystemMappingsAsync();
                        var store = await _db.StoreInfo.FirstOrDefaultAsync(s => s.StoreConfigId == 1);
                        string deliveryRevAcct = !string.IsNullOrEmpty(store?.DeliveryRevenueAccountId)
                            ? $"ID:{store.DeliveryRevenueAccountId}"
                            : $"ID:{await _accountingCore.GetRequiredMappedAccountAsync(Sportive.API.Utils.MappingKeys.DeliveryRevenue, mapDict)}";
                        
                        string receivablesAcct;
                        if (order.Customer?.MainAccountId != null)
                            receivablesAcct = $"ID:{order.Customer.MainAccountId}";
                        else
                            receivablesAcct = $"ID:{await _accountingCore.GetRequiredMappedAccountAsync(Sportive.API.Utils.MappingKeys.Customer, mapDict)}";

                        if (order.DeliveryFee > 0)
                        {
                            // 1. Debit Delivery Revenue
                            reversalLines.Add((
                                code: deliveryRevAcct,
                                debit: order.DeliveryFee,
                                credit: 0,
                                desc: $"عكس إيراد شحن لعدم التحصيل - فاتورة #{order.OrderNumber}"
                            ));

                            // 2. Credit Customer (Clear Debt)
                            reversalLines.Add((
                                code: receivablesAcct,
                                debit: 0,
                                credit: order.DeliveryFee,
                                desc: $"إسقاط مديونية شحن لعدم التحصيل - فاتورة #{order.OrderNumber}"
                            ));
                        }

                        await _accountingCore.PostEntryAsync(
                            type: JournalEntryType.Manual,
                            reference: $"REV-{returnEntry.Reference}",
                            description: $"عكس قيد شحن مرتجع لعدم التحصيل - فاتورة #{order.OrderNumber}",
                            date: TimeHelper.GetEgyptBusinessDayDate(collectionDate),
                            lines: reversalLines,
                            source: order.Source,
                            createdAt: currentSysTime,
                            orderId: order.Id,
                            branchId: order.BranchId
                        );
                    }
                }
            }
        }

        decimal netAmount = totalCollected - totalShippingCost;

        var invNumStr = !string.IsNullOrWhiteSpace(request.InvoiceNumber) ? $" - فاتورة: {request.InvoiceNumber}" : "";

        // ----------------------------------------------------
        // 1️⃣ القيد الأول: قيد تحصيل الصافي (بتاريخ التحصيل)
        // ----------------------------------------------------
        if (netAmount > 0)
        {
            var collectionLines = new List<(string code, decimal debit, decimal credit, string desc)>
            {
                // مدين: البنك / الخزينة / إنستا باي (بالصافي المحول)
                (cashAccountCode, netAmount, 0, $"تحصيل صافي مستحقات شحن {company.NameAr} - {orders.Count} طلبات{invNumStr}"),
                // دائن: حساب شركة الشحن (بالصافي المحول)
                ($"ID:{company.AccountId}", 0, netAmount, $"تسديد صافي متحصلات طلبات مجمعة{invNumStr}")
            };

            await _accountingCore.PostEntryAsync(
                type: JournalEntryType.ReceiptVoucher,
                reference: collectionRef,
                description: $"تسوية تحصيل صافي شركة الشحن: {company.NameAr}{invNumStr}",
                date: TimeHelper.GetEgyptBusinessDayDate(collectionDate),
                lines: collectionLines,
                source: OrderSource.Website,
                createdAt: currentSysTime,
                branchId: orders.FirstOrDefault()?.BranchId
            );
        }
        else if (netAmount < 0)
        {
            var deficitRef = $"SETTLE-DEFICIT-{company.Id}-{timestamp}";
            var deficitLines = new List<(string code, decimal debit, decimal credit, string desc)>
            {
                // مدين: حساب شركة الشحن
                ($"ID:{company.AccountId}", Math.Abs(netAmount), 0, $"سداد عجز شركة الشحن: {company.NameAr}{invNumStr}"),
                // دائن: الخزينة / البنك
                (cashAccountCode, 0, Math.Abs(netAmount), $"سداد عجز تسوية شحن {company.NameAr}{invNumStr}")
            };

            await _accountingCore.PostEntryAsync(
                type: JournalEntryType.PaymentVoucher,
                reference: deficitRef,
                description: $"سداد عجز تسوية شركة الشحن: {company.NameAr}{invNumStr}",
                date: TimeHelper.GetEgyptBusinessDayDate(collectionDate),
                lines: deficitLines,
                source: OrderSource.Website,
                createdAt: currentSysTime,
                branchId: orders.FirstOrDefault()?.BranchId
            );
        }

        // ----------------------------------------------------
        // 2️⃣ القيد الثاني: قيود مصروف خدمة التوصيل (مقسمة حسب تاريخ إنشاء الطلبات)
        // ----------------------------------------------------
        if (totalShippingCost > 0)
        {
            // Group orders by their creation date (Egypt Time)
            var ordersGroupedByDate = orders
                .GroupBy(o => Sportive.API.Utils.TimeHelper.GetEgyptBusinessDayDate(o.CreatedAt).Date)
                .OrderBy(g => g.Key)
                .ToList();

            int groupIndex = 1;
            foreach (var group in ordersGroupedByDate)
            {
                decimal groupShippingCost = group.Sum(o => o.ActualDeliveryCost);
                if (groupShippingCost <= 0) continue;

                var expenseRef = $"SETTLE-EXP-{company.Id}-{timestamp}-{groupIndex}";
                var expenseLines = new List<(string code, decimal debit, decimal credit, string desc)>
                {
                    // مدين: حساب مصروف خدمة التوصيل
                    (deliveryExpenseAccount, groupShippingCost, 0, $"مصاريف شحن وتوصيل لشركة {company.NameAr} ({group.Count()} طلبات){invNumStr}"),
                    // دائن: حساب شركة الشحن
                    ($"ID:{company.AccountId}", 0, groupShippingCost, $"إثبات استحقاق مصروف الشحن لشركة {company.NameAr}{invNumStr}")
                };

                await _accountingCore.PostEntryAsync(
                    type: JournalEntryType.PaymentVoucher,
                    reference: expenseRef,
                    description: $"إثبات مصروف خدمة توصيل شركة الشحن: {company.NameAr}{invNumStr}",
                    date: group.Key, // تسجيل القيد بتاريخ إنشاء الطلبات لضبط قائمة الدخل
                    lines: expenseLines,
                    source: OrderSource.Website,
                    createdAt: currentSysTime,
                    branchId: group.FirstOrDefault()?.BranchId
                );

                groupIndex++;
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new { success = true, totalSettled = netAmount, count = orders.Count });
    }

    [HttpPost("unsettle")]
    public async Task<IActionResult> UnsettleOrders([FromBody] UnsettleShippingRequest request)
    {
        if (request.OrderIds == null || !request.OrderIds.Any())
            return BadRequest("يجب تحديد الطلبات المراد إلغاء تسويتها.");

        var orders = await _db.Orders
            .Where(o => request.OrderIds.Contains(o.Id) && o.IsSettledWithCourier == true)
            .ToListAsync();

        if (!orders.Any())
            return BadRequest("لا توجد طلبات مسواة لإلغائها.");

        var references = orders
            .Select(o => o.CourierSettlementReference)
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct()
            .ToList();

        var journalEntriesToRemove = await _db.JournalEntries
            .Include(j => j.Lines)
            .Where(j => j.Reference != null && j.Reference.StartsWith("SETTLE-"))
            .ToListAsync();

        if (references.Any())
        {
            journalEntriesToRemove = journalEntriesToRemove
                .Where(j => references.Any(r => j.Reference != null && (j.Reference == r || (r != null && r.Length > 14 && j.Reference.EndsWith(r.Substring(r.Length - 14))))))
                .ToList();
        }

        if (journalEntriesToRemove.Any())
        {
            foreach (var entry in journalEntriesToRemove)
            {
                _db.JournalLines.RemoveRange(entry.Lines);
            }
            _db.JournalEntries.RemoveRange(journalEntriesToRemove);
        }

        foreach (var order in orders)
        {
            order.IsSettledWithCourier = false;
            order.CourierSettlementDate = null;
            order.CourierSettlementReference = null;
        }

        await _db.SaveChangesAsync();
        try { Hangfire.BackgroundJob.Enqueue<IAccountingService>(a => a.SyncEntityBalancesAsync()); } catch { }

        return Ok(new { success = true, count = orders.Count });
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
                string? id = order.BostaDeliveryId;
                string? trk = order.BostaTrackingNumber;
                string? refNum = order.OrderNumber;
                List<string> possibleEndpoints = new();
                if (!string.IsNullOrEmpty(id))
                {
                    possibleEndpoints.Add($"/api/v2/deliveries/{id}");
                    possibleEndpoints.Add($"/api/v0/deliveries/{id}");
                }
                if (!string.IsNullOrEmpty(trk))
                {
                    possibleEndpoints.Add($"/api/v2/deliveries/awb/{trk}");
                    possibleEndpoints.Add($"/api/v0/deliveries?trackingNumber={trk}");
                }
                if (!string.IsNullOrEmpty(refNum))
                {
                    possibleEndpoints.Add($"/api/v2/deliveries/business/{refNum}");
                }

                if (!debugLogs.Any(l => l.StartsWith("Base URL:")))
                    debugLogs.Add($"Base URL: {baseUrl}");

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


                        // Try to find the pricing object
                        var pricingObj = targetElement;
                        if (targetElement.TryGetProperty("pricing", out var pObj) && pObj.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            pricingObj = pObj;
                        }

                        // Extract individual fee components
                        decimal shipmentFees = pricingObj.TryGetProperty("shipmentFees", out var sProp) && sProp.TryGetDecimal(out var sVal) ? sVal : 0;
                        decimal codFees = pricingObj.TryGetProperty("cashOnDelivery", out var cProp) && cProp.TryGetDecimal(out var cVal) ? cVal : 0;
                        decimal returnFees = pricingObj.TryGetProperty("returnFees", out var rProp) && rProp.TryGetDecimal(out var rVal) ? rVal : 0;
                        decimal vat = pricingObj.TryGetProperty("vat", out var vProp) && vProp.TryGetDecimal(out var vVal) ? vVal : 0;
                        decimal insurance = pricingObj.TryGetProperty("insurance", out var iProp) && iProp.TryGetDecimal(out var iVal) ? iVal : 0;

                        if (shipmentFees > 0 || codFees > 0 || returnFees > 0)
                        {
                            if (vat == 0) 
                            {
                                // Calculate 14% VAT if not explicitly provided
                                vat = (shipmentFees + codFees + returnFees + insurance) * 0.14m;
                            }
                            foundCost = Math.Round(shipmentFees + codFees + returnFees + insurance + vat, 2);
                        }
                        else 
                        {
                            // Fallback to log backward search if pricing object is empty
                            if (targetElement.TryGetProperty("log", out var logArray) && logArray.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                for (int j = logArray.GetArrayLength() - 1; j >= 0; j--)
                                {
                                    var logItem = logArray[j];
                                    if (logItem.TryGetProperty("actionsList", out var actionsList) && 
                                        actionsList.TryGetProperty("pricing", out var logPricing) && 
                                        logPricing.TryGetProperty("after", out var pricingAfter) &&
                                        pricingAfter.TryGetProperty("priceAfterVat", out var priceAfterVatProp) && 
                                        priceAfterVatProp.TryGetDecimal(out var logPrice))
                                    {
                                        foundCost = logPrice; // No rounding
                                        break;
                                    }
                                }
                            }

                            if (foundCost == 0 && targetElement.TryGetProperty("price", out var priceProp) && priceProp.TryGetDecimal(out var priceObjVal))
                            {
                                foundCost = priceObjVal;
                            }
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

        int failedCount = orders.Count - successCount;

        if (successCount > 0)
        {
            await _db.SaveChangesAsync();
            return Ok(new { success = true, syncedCount = successCount, failedCount = failedCount, totalRequested = orders.Count });
        }
        else
        {
            var importantLogs = debugLogs.Where(l => l.Contains("Failed API Call") || l.Contains("RAW:") || l.Contains("Exception:")).Take(5);
            string errorDetails = importantLogs.Any() ? string.Join(" \n ", importantLogs) : "لا يوجد تفاصيل";
            return BadRequest($"فشل تحديث أسعار بوسطة لجميع الطلبات المحددة (عدد {failedCount}). \n تفاصيل: \n {errorDetails}");
        }
    }
}

public class SyncBostaPricesRequest
{
    public List<int> OrderIds { get; set; } = new();
}

public class UnsettleShippingRequest
{
    public List<int> OrderIds { get; set; } = new();
}

public class SettleShippingRequest
{
    public int ShippingCompanyId { get; set; }
    public List<int> OrderIds { get; set; } = new();
    public List<SettleOrderDto> Orders { get; set; } = new();
    public PaymentMethod Method { get; set; } = PaymentMethod.Bank;
    public int? TargetAccountId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public DateTime? CollectionDate { get; set; }
}

public class SettleOrderDto
{
    public int OrderId { get; set; }
    public decimal ActualDeliveryCost { get; set; }
    public decimal? CollectedAmount { get; set; }
}
