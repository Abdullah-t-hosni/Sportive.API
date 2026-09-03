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
[Route("api/shippingsettlements")]
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
        var company = await _db.ShippingCompanies.FindAsync(companyId);
        bool isAsCompany = company != null && (company.NameAr.Contains("AS") || company.NameAr.Contains("A&S") || (company.NameEn != null && (company.NameEn.Contains("AS") || company.NameEn.Contains("A&S"))));
        
        var q = _db.Orders
            .Include(o => o.Customer)
            .Where(o => o.FulfillmentType != FulfillmentType.Pickup &&
                        o.ShippingType != "Pickup" &&
                        (o.ShippingCompanyId == companyId || (isAsCompany && o.ShippingCompanyId == null && o.ShippingType != "Bosta" && o.BostaDeliveryId == null && o.BostaTrackingNumber == null && (o.ShippingCarrierName == null || (!o.ShippingCarrierName.Contains("الجوهري") && !o.ShippingCarrierName.Contains("بوسطة") && !o.ShippingCarrierName.Contains("Bosta") && !o.ShippingCarrierName.Contains("استلام") && !o.ShippingCarrierName.Contains("فرع"))))) && 
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

        bool isAsCompany = company != null && (company.NameAr.Contains("AS", StringComparison.OrdinalIgnoreCase) || company.NameAr.Contains("A&S", StringComparison.OrdinalIgnoreCase) || (company.NameEn != null && (company.NameEn.Contains("AS", StringComparison.OrdinalIgnoreCase) || company.NameEn.Contains("A&S", StringComparison.OrdinalIgnoreCase))));

        var orders = await _db.Orders
            .Include(o => o.Customer)
            .Where(o => orderIds.Contains(o.Id) && 
                        o.FulfillmentType != FulfillmentType.Pickup &&
                        o.ShippingType != "Pickup" &&
                        (o.ShippingCompanyId == request.ShippingCompanyId || (isAsCompany && o.ShippingCompanyId == null)) &&
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
        }

        decimal netAmount = totalCollected - totalShippingCost;

        var invNumStr = !string.IsNullOrWhiteSpace(request.InvoiceNumber) ? $" - فاتورة: {request.InvoiceNumber}" : "";

        var fullMapDict = await _accountingCore.GetSafeSystemMappingsAsync();
        var defaultCustomerAcctId = await _accountingCore.GetRequiredMappedAccountAsync(Utils.MappingKeys.Customer, fullMapDict);
        string deliveryRevenueAccount = storeSettings?.DeliveryRevenueAccountId != null
            ? $"ID:{storeSettings.DeliveryRevenueAccountId}"
            : $"ID:{await _accountingCore.GetRequiredMappedAccountAsync(Utils.MappingKeys.DeliveryRevenue, fullMapDict)}";

        // ----------------------------------------------------
        // 1️⃣ قيد المصروف (بتاريخ الفاتورة)
        // ----------------------------------------------------
        if (totalShippingCost > 0)
        {
            var expenseRef = $"SETTLE-EXP-{company.Id}-{timestamp}";
            var expenseLines = new List<(string code, decimal debit, decimal credit, string desc)>
            {
                (deliveryExpenseAccount, totalShippingCost, 0, $"مصاريف شحن وتوصيل - {company.NameAr}{invNumStr}"),
                ($"ID:{company.AccountId}", 0, totalShippingCost, $"استحقاق مصاريف شحن لشركة - {company.NameAr}{invNumStr}")
            };

            await _accountingCore.PostEntryAsync(
                type: JournalEntryType.Manual,
                reference: expenseRef,
                description: $"قيد مصروف تسوية شحن: {company.NameAr}{invNumStr}",
                date: TimeHelper.GetEgyptBusinessDayDate(invoiceDate),
                lines: expenseLines,
                source: OrderSource.Website,
                createdAt: currentSysTime,
                branchId: orders.FirstOrDefault()?.BranchId
            );
        }

        // ----------------------------------------------------
        // 2️⃣ قيد التسوية ونقل المديونية للطلبات المحصلة (بتاريخ التسوية)
        // ----------------------------------------------------
        if (totalCollected > 0)
        {
            var deliveredOrderIds = orders.Select(o => o.Id).ToList();
            var alreadyTransferredOrderIds = await _db.JournalEntries
                .Where(e => e.OrderId.HasValue && deliveredOrderIds.Contains(e.OrderId.Value) &&
                            e.Reference != null && (e.Reference.StartsWith("DELV-CUST-") || e.Reference.StartsWith("SETTLE-CUST-")))
                .Where(e => e.Status == JournalEntryStatus.Posted)
                .Select(e => e.OrderId!.Value)
                .ToListAsync();

            var pendingTransferOrders = orders.Where(o => !alreadyTransferredOrderIds.Contains(o.Id)).ToList();
            decimal pendingCollected = pendingTransferOrders.Sum(o => {
                var reqOrder = request.Orders?.FirstOrDefault(x => x.OrderId == o.Id);
                return reqOrder?.CollectedAmount ?? o.TotalAmount;
            });

            if (pendingCollected > 0 && pendingTransferOrders.Any())
            {
                var settlementRef = $"SETTLE-CUST-{company.Id}-{timestamp}";
                var settlementLines = new List<(string code, decimal debit, decimal credit, string desc)>();
                
                // إجمالي المبلغ مدين لشركة الشحن
                settlementLines.Add(($"ID:{company.AccountId}", pendingCollected, 0, $"تحصيل مديونية طلبات مجمعة - {company.NameAr}{invNumStr}"));

                // التفقيط الدائن للعملاء
                foreach (var order in pendingTransferOrders)
                {
                    var reqOrder = request.Orders?.FirstOrDefault(x => x.OrderId == order.Id);
                    decimal collected = reqOrder?.CollectedAmount ?? order.TotalAmount;
                    if (collected > 0)
                    {
                        string customerAcct = order.Customer?.MainAccountId != null 
                            ? $"ID:{order.Customer.MainAccountId}" 
                            : $"ID:{defaultCustomerAcctId}";
                        
                        settlementLines.Add((customerAcct, 0, collected, $"إقفال مديونية طلب #{order.OrderNumber}"));
                    }
                }

                if (settlementLines.Count > 1)
                {
                    await _accountingCore.PostEntryAsync(
                        type: JournalEntryType.Manual,
                        reference: settlementRef,
                        description: $"قيد تسوية وتحصيل من العملاء لشركة الشحن: {company.NameAr}{invNumStr}",
                        date: TimeHelper.GetEgyptBusinessDayDate(collectionDate),
                        lines: settlementLines,
                        source: OrderSource.Website,
                        createdAt: currentSysTime,
                        branchId: orders.FirstOrDefault()?.BranchId
                    );
                }
            }
        }

        // ----------------------------------------------------
        // قيد تسوية للطلبات التي لم تُحصل (مرتجع من المندوب) (بتاريخ التسوية)
        // ----------------------------------------------------
        var uncollectedOrders = orders.Where(o => 
        {
            var r = request.Orders?.FirstOrDefault(x => x.OrderId == o.Id);
            decimal col = r?.CollectedAmount ?? o.TotalAmount;
            return col == 0 && o.DeliveryFee > 0;
        }).ToList();

        if (uncollectedOrders.Any())
        {
            var uncollectedRef = $"SETTLE-UNCOLLECTED-{company.Id}-{timestamp}";
            var uncollectedLines = new List<(string code, decimal debit, decimal credit, string desc)>();
            
            decimal totalDeliveryFee = uncollectedOrders.Sum(o => o.DeliveryFee);

            // مدين لإيراد التوصيل بإجمالي الإيراد المرتجع
            uncollectedLines.Add((deliveryRevenueAccount, totalDeliveryFee, 0, $"عكس إيراد توصيل لطلبات لم تُحصل - {company.NameAr}{invNumStr}"));

            // التفقيط الدائن للعملاء
            foreach (var order in uncollectedOrders)
            {
                string customerAcct = order.Customer?.MainAccountId != null 
                    ? $"ID:{order.Customer.MainAccountId}" 
                    : $"ID:{defaultCustomerAcctId}";
                
                uncollectedLines.Add((customerAcct, 0, order.DeliveryFee, $"إلغاء مديونية شحن لعدم الاستلام طلب #{order.OrderNumber}"));
            }

            if (uncollectedLines.Count > 1)
            {
                await _accountingCore.PostEntryAsync(
                    type: JournalEntryType.Manual,
                    reference: uncollectedRef,
                    description: $"قيد إلغاء إيرادات توصيل لطلبات لم تُحصل: {company.NameAr}{invNumStr}",
                    date: TimeHelper.GetEgyptBusinessDayDate(collectionDate),
                    lines: uncollectedLines,
                    source: OrderSource.Website,
                    createdAt: currentSysTime,
                    branchId: uncollectedOrders.FirstOrDefault()?.BranchId
                );
            }
        }

        // ----------------------------------------------------
        // 3️⃣ قيد تحصيل الصافي (بتاريخ التحصيل)
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
        // Removed the automatic deficit payment voucher (netAmount < 0) as requested by the user.



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

        // Also fetch any OTHER orders that share this same settlement reference
        // because we are about to delete the entire batch Journal Entry!
        var allOrdersSharingReferences = new List<Order>();
        if (references.Any())
        {
            allOrdersSharingReferences = await _db.Orders
                .Where(o => o.CourierSettlementReference != null && references.Contains(o.CourierSettlementReference))
                .ToListAsync();
        }

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

        // Unsettle ALL orders that share the deleted references to avoid data corruption (Settled without JE)
        var ordersToUnsettle = allOrdersSharingReferences.Any() ? allOrdersSharingReferences : orders;
        
        foreach (var order in ordersToUnsettle)
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

    [HttpPost("bulk-settle-historical-range")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> BulkSettleHistoricalRange([FromBody] BulkHistoricalSettlementDto dto)
    {
        if (dto.FromDate == default || dto.ToDate == default)
            return BadRequest("يرجى تحديد تاريخ بداية ونهاية صحيح للفترة المطلوب تسويتها.");

        var from = dto.FromDate.Date;
        var to = dto.ToDate.Date.AddDays(1).AddTicks(-1);

        var validStatuses = new[] { OrderStatus.Delivered, OrderStatus.Returned, OrderStatus.PartiallyReturned };

        var query = _db.Orders
            .Where(o => validStatuses.Contains(o.Status) && 
                        !o.IsSettledWithCourier && 
                        o.Source != OrderSource.POS &&
                        o.FulfillmentType != FulfillmentType.Pickup &&
                        o.ShippingType != "Pickup" &&
                        o.CreatedAt >= from && 
                        o.CreatedAt <= to);

        if (dto.ShippingCompanyId.HasValue && dto.ShippingCompanyId.Value > 0)
        {
            query = query.Where(o => o.ShippingCompanyId == dto.ShippingCompanyId.Value);
        }

        ShippingCompany? targetCompany = null;
        if (dto.ShippingCompanyId.HasValue && dto.ShippingCompanyId.Value > 0)
        {
            targetCompany = await _db.ShippingCompanies.FindAsync(dto.ShippingCompanyId.Value);
        }

        if (targetCompany == null)
        {
            targetCompany = await _db.ShippingCompanies.FirstOrDefaultAsync(c => 
                c.NameAr.Contains("AS") || 
                c.NameAr.Contains("A&S") ||
                (c.NameEn != null && (c.NameEn.Contains("AS") || c.NameEn.Contains("A&S")))
            );
        }

        var orders = await query.ToListAsync();

        int count = 0;
        var now = TimeHelper.GetEgyptTime();
        string refStr = !string.IsNullOrWhiteSpace(dto.Reference) ? dto.Reference : "تسوية تاريخية سابقة (بدون قيد مكرر)";

        foreach (var o in orders)
        {
            if (targetCompany != null)
            {
                o.ShippingCompanyId = targetCompany.Id;
                o.ShippingCarrierName = targetCompany.NameAr;
            }
            o.IsSettledWithCourier = true;
            o.CourierSettlementDate = now;
            o.CourierSettlementReference = refStr;
            count++;
        }

        if (count > 0)
        {
            await _db.SaveChangesAsync();
        }

        return Ok(new { 
            success = true, 
            count, 
            shippingCompanyName = targetCompany?.NameAr ?? "شركة شحن AS",
            message = $"تم ربط وتسوية حالة {count} طلب أونلاين مسلم بشركة ({targetCompany?.NameAr ?? "AS"}) في الفترة من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd} بدون قيود محاسبية مكررة." 
        });
    }

    [HttpGet("historical-summary")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetHistoricalSummary()
    {
        var firstSettledOrder = await _db.Orders
            .AsNoTracking()
            .Where(o => o.IsSettledWithCourier && 
                        (o.CourierSettlementDate != null || o.CourierSettlementReference != null) &&
                        o.Source != OrderSource.POS &&
                        o.FulfillmentType != FulfillmentType.Pickup &&
                        o.ShippingType != "Pickup")
            .OrderBy(o => o.CourierSettlementDate ?? o.CreatedAt)
            .FirstOrDefaultAsync();

        DateTime? firstSettledDate = firstSettledOrder?.CourierSettlementDate ?? firstSettledOrder?.CreatedAt;

        var earliestOrder = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Source != OrderSource.POS &&
                        o.FulfillmentType != FulfillmentType.Pickup &&
                        o.ShippingType != "Pickup")
            .OrderBy(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        DateTime earliestCreated = earliestOrder?.CreatedAt ?? TimeHelper.GetEgyptTime();

        DateTime cutoff = firstSettledDate ?? TimeHelper.GetEgyptTime();

        var validStatuses = new[] { OrderStatus.Delivered, OrderStatus.Returned, OrderStatus.PartiallyReturned };

        var pendingBeforeCutoff = await _db.Orders
            .AsNoTracking()
            .Where(o => validStatuses.Contains(o.Status) && 
                        !o.IsSettledWithCourier && 
                        o.Source != OrderSource.POS &&
                        o.FulfillmentType != FulfillmentType.Pickup &&
                        o.ShippingType != "Pickup" &&
                        o.CreatedAt < cutoff)
            .ToListAsync();

        var asCompany = await _db.ShippingCompanies.FirstOrDefaultAsync(c => 
            c.NameAr.Contains("AS") || 
            c.NameAr.Contains("A&S") ||
            (c.NameEn != null && (c.NameEn.Contains("AS") || c.NameEn.Contains("A&S")))
        );

        return Ok(new {
            hasSettlements = firstSettledOrder != null,
            firstSettledOrderNumber = firstSettledOrder?.OrderNumber,
            firstSettledDate = firstSettledDate?.ToString("yyyy-MM-dd HH:mm"),
            firstSettledReference = firstSettledOrder?.CourierSettlementReference,
            earliestOrderDate = earliestCreated.ToString("yyyy-MM-dd"),
            suggestedFromDate = earliestCreated.ToString("yyyy-MM-dd"),
            suggestedToDate = firstSettledDate?.AddDays(-1).ToString("yyyy-MM-dd") ?? TimeHelper.GetEgyptTime().ToString("yyyy-MM-dd"),
            pendingHistoricalCount = pendingBeforeCutoff.Count,
            pendingHistoricalTotalAmount = pendingBeforeCutoff.Sum(o => o.TotalAmount),
            asCompanyId = asCompany?.Id,
            asCompanyName = asCompany?.NameAr ?? "شركة شحن AS"
        });
    }

    [HttpPost("bulk-settle-before-first")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> BulkSettleBeforeFirst()
    {
        var firstSettledOrder = await _db.Orders
            .AsNoTracking()
            .Where(o => o.IsSettledWithCourier && 
                        (o.CourierSettlementDate != null || o.CourierSettlementReference != null) &&
                        o.Source != OrderSource.POS &&
                        o.FulfillmentType != FulfillmentType.Pickup &&
                        o.ShippingType != "Pickup")
            .OrderBy(o => o.CourierSettlementDate ?? o.CreatedAt)
            .FirstOrDefaultAsync();

        DateTime cutoff = firstSettledOrder?.CourierSettlementDate ?? firstSettledOrder?.CreatedAt ?? TimeHelper.GetEgyptTime();

        var validStatuses = new[] { OrderStatus.Delivered, OrderStatus.Returned, OrderStatus.PartiallyReturned };

        var ordersToSettle = await _db.Orders
            .Where(o => validStatuses.Contains(o.Status) && 
                        !o.IsSettledWithCourier && 
                        o.Source != OrderSource.POS &&
                        o.FulfillmentType != FulfillmentType.Pickup &&
                        o.ShippingType != "Pickup" &&
                        o.CreatedAt < cutoff)
            .ToListAsync();

        var asCompany = await _db.ShippingCompanies.FirstOrDefaultAsync(c => 
            c.NameAr.Contains("AS") || 
            c.NameAr.Contains("A&S") ||
            (c.NameEn != null && (c.NameEn.Contains("AS") || c.NameEn.Contains("A&S")))
        );

        int count = 0;
        var now = TimeHelper.GetEgyptTime();
        string refStr = $"تسوية تاريخية مجمعة للطلبات السابقة لأول تسوية ({cutoff:yyyy-MM-dd})";

        foreach (var o in ordersToSettle)
        {
            if (asCompany != null)
            {
                o.ShippingCompanyId = asCompany.Id;
                o.ShippingCarrierName = asCompany.NameAr;
            }
            o.IsSettledWithCourier = true;
            o.CourierSettlementDate = now;
            o.CourierSettlementReference = refStr;
            count++;
        }

        if (count > 0)
        {
            await _db.SaveChangesAsync();
        }

        return Ok(new {
            success = true,
            count,
            cutoffDate = cutoff.ToString("yyyy-MM-dd"),
            message = $"تم ربط وتسوية حالة {count} طلب أونلاين مسلم بشركة ({asCompany?.NameAr ?? "AS"}) قبل تاريخ ({cutoff:yyyy-MM-dd}) بدون قيود محاسبية مكررة."
        });
    }

    [HttpPost("import-as-excel")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> ImportAsExcel([FromBody] ImportAsExcelDto dto)
    {
        if (dto.Rows == null || !dto.Rows.Any())
            return BadRequest("شيت الإكسيل فارغ أو لا يحتوي على أي شحنات.");

        var asCompany = await _db.ShippingCompanies.FirstOrDefaultAsync(c => 
            c.NameAr.Contains("AS") || 
            c.NameAr.Contains("A&S") ||
            (c.NameEn != null && (c.NameEn.Contains("AS") || c.NameEn.Contains("A&S")))
        );

        int settledCount = 0;
        int updatedStatusCount = 0;
        decimal totalCodSettled = 0;
        decimal totalFeesRecorded = 0;
        var now = TimeHelper.GetEgyptTime();

        var allOrders = await _db.Orders
            .Include(o => o.Customer)
            .Where(o => o.Source != OrderSource.POS && 
                        o.FulfillmentType != FulfillmentType.Pickup && 
                        o.ShippingType != "Pickup")
            .ToListAsync();

        var unmatchedList = new List<object>();

        foreach (var row in dto.Rows)
        {
            string waybill = row.WaybillNo?.Trim() ?? "";
            string receiver = row.ReceiverName?.Trim() ?? "";
            bool isReturnSign = row.SigningStatus?.Equals("Return Sign", StringComparison.OrdinalIgnoreCase) == true;
            decimal cod = row.CodAmount;
            decimal fees = row.TotalFees;

            // 1. Try match by tracking number
            Order? matchedOrder = null;
            if (!string.IsNullOrEmpty(waybill))
            {
                matchedOrder = allOrders.FirstOrDefault(o => 
                    (!string.IsNullOrEmpty(o.ShippingTrackingNumber) && o.ShippingTrackingNumber.Equals(waybill, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(o.BostaTrackingNumber) && o.BostaTrackingNumber.Equals(waybill, StringComparison.OrdinalIgnoreCase))
                );
            }

            // 2. Try match by Receiver Name & amount if tracking not matched
            if (matchedOrder == null && !string.IsNullOrEmpty(receiver))
            {
                matchedOrder = allOrders.FirstOrDefault(o => 
                    !o.IsSettledWithCourier &&
                    (o.Customer != null && !string.IsNullOrEmpty(o.Customer.FullName) && (o.Customer.FullName.Contains(receiver) || receiver.Contains(o.Customer.FullName)))
                );
            }

            if (matchedOrder != null)
            {
                if (asCompany != null)
                {
                    matchedOrder.ShippingCompanyId = asCompany.Id;
                    matchedOrder.ShippingCarrierName = asCompany.NameAr;
                }
                
                if (!string.IsNullOrEmpty(waybill))
                {
                    matchedOrder.ShippingTrackingNumber = waybill;
                }

                // Take exact AS shipping fee directly from Excel row
                matchedOrder.ActualDeliveryCost = fees;
                matchedOrder.IsSettledWithCourier = true;
                matchedOrder.CourierSettlementDate = now;
                matchedOrder.CourierSettlementReference = $"تسوية شيت AS الرسمية (بوليصة: {waybill})";

                // Update order status & collected amount directly from Excel row
                if (isReturnSign)
                {
                    if (matchedOrder.Status != OrderStatus.Returned && matchedOrder.Status != OrderStatus.PartiallyReturned)
                    {
                        matchedOrder.Status = OrderStatus.Returned;
                        updatedStatusCount++;
                    }
                }
                else if (row.SigningStatus?.Equals("Normal Sign", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (matchedOrder.Status != OrderStatus.Delivered)
                    {
                        matchedOrder.Status = OrderStatus.Delivered;
                        updatedStatusCount++;
                    }
                    if (cod > 0 && Math.Abs(matchedOrder.TotalAmount - cod) > 0.01m)
                    {
                        matchedOrder.TotalAmount = cod;
                    }
                }

                settledCount++;
                totalCodSettled += matchedOrder.Status == OrderStatus.Delivered ? matchedOrder.TotalAmount : 0;
                totalFeesRecorded += fees;
            }
            else
            {
                unmatchedList.Add(new { waybill, receiver, cod, fees, status = row.SigningStatus });
            }
        }

        if (settledCount > 0)
        {
            await _db.SaveChangesAsync();
        }

        return Ok(new {
            success = true,
            totalInExcel = dto.Rows.Count,
            settledCount,
            updatedStatusCount,
            totalCodSettled,
            totalFeesRecorded,
            unmatchedCount = unmatchedList.Count,
            unmatchedList,
            message = $"تم بنجاح مطابقة وتسوية {settledCount} طلب من شيت AS وتحديث التكاليف والمبالغ المحصلة بدون قيود مكررة."
        });
    }

    [HttpPost("import-bosta-excel")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> ImportBostaExcel([FromBody] ImportBostaExcelDto dto)
    {
        if (dto.Rows == null || !dto.Rows.Any())
            return BadRequest("شيت بوسطة فارغ أو لا يحتوي على أي شحنات.");

        var bostaCompany = await _db.ShippingCompanies.FirstOrDefaultAsync(c => 
            c.NameAr.Contains("بوسطة") || 
            (c.NameEn != null && c.NameEn.Contains("Bosta"))
        );

        int settledCount = 0;
        int updatedStatusCount = 0;
        decimal totalCodSettled = 0;
        var now = TimeHelper.GetEgyptTime();

        var allOrders = await _db.Orders
            .Include(o => o.Customer)
            .Where(o => o.Source != OrderSource.POS && 
                        o.FulfillmentType != FulfillmentType.Pickup && 
                        o.ShippingType != "Pickup")
            .ToListAsync();

        var unmatchedList = new List<object>();

        foreach (var row in dto.Rows)
        {
            string tracking = row.TrackingNumber?.Trim() ?? "";
            string refNo = row.BusinessReferenceNumber?.Trim() ?? "";
            string deliveryState = row.DeliveryState?.Trim() ?? "";
            decimal cod = row.CodAmount;

            // 1. Match by Business Reference Number (OrderNumber like SPT-2608-xxxx)
            Order? matchedOrder = null;
            if (!string.IsNullOrEmpty(refNo))
            {
                matchedOrder = allOrders.FirstOrDefault(o => 
                    o.OrderNumber.Equals(refNo, StringComparison.OrdinalIgnoreCase)
                );
            }

            // 2. Match by Tracking Number if not matched by ref
            if (matchedOrder == null && !string.IsNullOrEmpty(tracking))
            {
                matchedOrder = allOrders.FirstOrDefault(o => 
                    (!string.IsNullOrEmpty(o.ShippingTrackingNumber) && o.ShippingTrackingNumber.Equals(tracking, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(o.BostaTrackingNumber) && o.BostaTrackingNumber.Equals(tracking, StringComparison.OrdinalIgnoreCase))
                );
            }

            if (matchedOrder != null)
            {
                if (bostaCompany != null)
                {
                    matchedOrder.ShippingCompanyId = bostaCompany.Id;
                    matchedOrder.ShippingCarrierName = bostaCompany.NameAr;
                }
                matchedOrder.ShippingType = "Bosta";
                
                if (!string.IsNullOrEmpty(tracking))
                {
                    matchedOrder.ShippingTrackingNumber = tracking;
                    matchedOrder.BostaTrackingNumber = tracking;
                }

                // Update Status from Bosta Delivery State
                string stateLower = deliveryState.ToLower();
                if (stateLower.Contains("delivered"))
                {
                    if (matchedOrder.Status != OrderStatus.Delivered)
                    {
                        matchedOrder.Status = OrderStatus.Delivered;
                        updatedStatusCount++;
                    }
                    matchedOrder.IsSettledWithCourier = true;
                    matchedOrder.CourierSettlementDate = now;
                    matchedOrder.CourierSettlementReference = $"تسوية شيت بوسطة (بوليصة: {tracking})";
                    if (cod > 0 && Math.Abs(matchedOrder.TotalAmount - cod) > 0.01m)
                    {
                        matchedOrder.TotalAmount = cod;
                    }
                    settledCount++;
                    totalCodSettled += matchedOrder.TotalAmount;
                }
                else if (stateLower.Contains("return"))
                {
                    if (matchedOrder.Status != OrderStatus.Returned && matchedOrder.Status != OrderStatus.PartiallyReturned)
                    {
                        matchedOrder.Status = OrderStatus.Returned;
                        updatedStatusCount++;
                    }
                    matchedOrder.IsSettledWithCourier = true;
                    matchedOrder.CourierSettlementDate = now;
                    matchedOrder.CourierSettlementReference = $"مرتجع شيت بوسطة (بوليصة: {tracking})";
                }
                else if (stateLower.Contains("out for delivery") || stateLower.Contains("pickup"))
                {
                    if (matchedOrder.Status != OrderStatus.OutForDelivery)
                    {
                        matchedOrder.Status = OrderStatus.OutForDelivery;
                        updatedStatusCount++;
                    }
                }
            }
            else
            {
                unmatchedList.Add(new { tracking, refNo, deliveryState, cod });
            }
        }

        if (settledCount > 0 || updatedStatusCount > 0)
        {
            await _db.SaveChangesAsync();
        }

        return Ok(new {
            success = true,
            totalInExcel = dto.Rows.Count,
            settledCount,
            updatedStatusCount,
            totalCodSettled,
            unmatchedCount = unmatchedList.Count,
            unmatchedList,
            message = $"تم بنجاح مطابقة وتسوية {settledCount} طلب وتحديث حالة {updatedStatusCount} طلب من شيت بوسطة."
        });
    }
}

public class ImportAsExcelDto
{
    public List<AsExcelRowDto> Rows { get; set; } = new();
}

public class AsExcelRowDto
{
    public string? WaybillNo { get; set; }
    public string? ReceiverName { get; set; }
    public string? SigningStatus { get; set; }
    public decimal CodAmount { get; set; }
    public decimal TotalFees { get; set; }
}

public class ImportBostaExcelDto
{
    public List<BostaExcelRowDto> Rows { get; set; } = new();
}

public class BostaExcelRowDto
{
    public string? TrackingNumber { get; set; }
    public string? BusinessReferenceNumber { get; set; }
    public string? DeliveryState { get; set; }
    public decimal CodAmount { get; set; }
}

public class BulkHistoricalSettlementDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int? ShippingCompanyId { get; set; }
    public string? Reference { get; set; }
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
