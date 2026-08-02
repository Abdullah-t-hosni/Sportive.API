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
                o.BostaTrackingNumber
            })
            .ToListAsync();

        return Ok(orders);
    }

    [HttpPost("settle")]
    public async Task<IActionResult> SettleOrders([FromBody] SettleShippingRequest request)
    {
        if (request == null || request.OrderIds == null || !request.OrderIds.Any())
            return BadRequest("يجب تحديد الطلبات المراد تسويتها.");

        var company = await _db.ShippingCompanies.FindAsync(request.ShippingCompanyId);
        if (company == null || company.AccountId == null)
            return BadRequest("شركة الشحن غير موجودة أو غير مربوطة بحساب في الدليل المحاسبي.");

        var orders = await _db.Orders
            .Where(o => request.OrderIds.Contains(o.Id) && 
                        o.ShippingCompanyId == request.ShippingCompanyId &&
                        o.IsSettledWithCourier == false)
            .ToListAsync();

        if (!orders.Any())
            return BadRequest("لا توجد طلبات متاحة للتسوية أو تم تسويتها مسبقاً.");

        decimal totalSettled = orders.Sum(o => o.TotalAmount);

        // Get Cash Account from Mapping
        var mapDict = await _accountingCore.GetSafeSystemMappingsAsync();
        var cashAccountCode = await _accountingCore.GetMappedCashAccountAsync(request.Method, OrderSource.Website, mapDict);

        // إنشاء قيد التسوية (سند قبض من شركة الشحن)
        var reference = $"SETTLE-SHP-{company.Id}-{DateTime.Now:yyyyMMddHHmmss}";
        var lines = new List<(string code, decimal debit, decimal credit, string desc)>
        {
            // مدين: حساب الخزينة/البنك اللي تم تحويل الفلوس عليه
            (cashAccountCode, totalSettled, 0, $"تسوية متحصلات من شركة شحن: {company.NameAr} - لعدد {orders.Count} طلب"),
            
            // دائن: حساب شركة الشحن (تقليل مديونيتهم)
            ($"ID:{company.AccountId}", 0, totalSettled, $"تسديد متحصلات طلبات مجمعة")
        };

        // تسجيل القيد
        await _accountingCore.PostEntryAsync(
            type: JournalEntryType.ReceiptVoucher,
            reference: reference,
            description: $"تسوية مدفوعات شركة الشحن: {company.NameAr}",
            date: TimeHelper.GetEgyptBusinessDayDate(DateTime.UtcNow),
            lines: lines,
            source: OrderSource.Website
        );

        // تحديث حالة الطلبات
        foreach (var order in orders)
        {
            order.IsSettledWithCourier = true;
            order.CourierSettlementDate = TimeHelper.GetEgyptTime();
        }

        await _db.SaveChangesAsync();

        return Ok(new { success = true, totalSettled, count = orders.Count });
    }
}

public class SettleShippingRequest
{
    public int ShippingCompanyId { get; set; }
    public List<int> OrderIds { get; set; } = new();
    public PaymentMethod Method { get; set; } = PaymentMethod.Bank;
}
