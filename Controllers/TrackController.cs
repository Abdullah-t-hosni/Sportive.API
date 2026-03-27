using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

/// <summary>
/// GET /api/track?orderNumber=SZ-xxx&phone=01xxxxxxxx
/// Endpoint عام — بدون تسجيل دخول
/// يرجع حالة الطلب إذا تطابق رقم الطلب + التليفون
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TrackController : ControllerBase
{
    private readonly AppDbContext _db;
    public TrackController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Track(
        [FromQuery] string orderNumber,
        [FromQuery] string phone)
    {
        if (string.IsNullOrWhiteSpace(orderNumber) || string.IsNullOrWhiteSpace(phone))
            return BadRequest(new { message = "رقم الطلب والتليفون مطلوبان" });

        var normalizedPhone = NormalizePhone(phone.Trim());
        var normalizedOrder = orderNumber.Trim().ToUpper();

        var order = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p.Images.Where(img => img.IsMain && !img.IsDeleted))
            .Include(o => o.DeliveryAddress)
            .Include(o => o.StatusHistory.OrderByDescending(h => h.CreatedAt))
            .Where(o => !o.IsDeleted
                     && o.OrderNumber.ToUpper() == normalizedOrder)
            .FirstOrDefaultAsync();

        if (order == null)
            return NotFound(new { message = "لا يوجد طلب بهذا الرقم" });

        // تحقق من التليفون
        var customerPhone = NormalizePhone(order.Customer?.Phone ?? "");
        if (customerPhone != normalizedPhone)
            return NotFound(new { message = "رقم الطلب أو التليفون غير صحيح" });

        // ترتيب خطوات timeline بالحالات الممكنة
        var timeline = BuildTimeline(order);

        return Ok(new
        {
            orderNumber  = order.OrderNumber,
            status       = order.Status.ToString(),
            statusAr     = GetStatusAr(order.Status),
            statusColor  = GetStatusColor(order.Status),
            isActive     = order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Returned,
            fulfillment  = order.FulfillmentType.ToString(),
            fulfillmentAr= order.FulfillmentType == FulfillmentType.Delivery ? "توصيل للمنزل" : "استلام من الفرع",
            paymentMethod= GetPaymentAr(order.PaymentMethod),
            paymentStatus= GetPaymentStatusAr(order.PaymentStatus),
            paymentPaid  = order.PaymentStatus == PaymentStatus.Paid,
            createdAt    = order.CreatedAt,
            estimatedDelivery = order.EstimatedDeliveryDate,
            actualDelivery    = order.ActualDeliveryDate,
            pickupScheduled   = order.PickupScheduledAt,
            deliveryAddress   = order.DeliveryAddress == null ? null : new {
                street = order.DeliveryAddress.Street,
                city   = order.DeliveryAddress.City,
                area   = order.DeliveryAddress.District,
            },
            pricing = new {
                subtotal = order.SubTotal,
                discount = order.DiscountAmount,
                delivery = order.DeliveryFee,
                total    = order.TotalAmount,
            },
            items = order.Items.Select(i => new {
                name    = i.ProductNameAr,
                nameEn  = i.ProductNameEn,
                size    = i.Size,
                color   = i.Color,
                qty     = i.Quantity,
                price   = i.TotalPrice,
                image   = i.Product?.Images.FirstOrDefault()?.ImageUrl,
            }),
            timeline,
            customerName = order.Customer?.FullName,
        });
    }

    // ── Timeline ─────────────────────────────────────────
    private static List<object> BuildTimeline(Order order)
    {
        // الخطوات الممكنة حسب نوع الطلب
        var steps = order.FulfillmentType == FulfillmentType.Delivery
            ? new[]
            {
                (OrderStatus.Pending,         "تم استلام الطلب",    "Pending"),
                (OrderStatus.Confirmed,       "تم تأكيد الطلب",    "Confirmed"),
                (OrderStatus.Processing,      "جاري التحضير",      "Processing"),
                (OrderStatus.OutForDelivery,  "خرج للتوصيل",       "OutForDelivery"),
                (OrderStatus.Delivered,       "تم التوصيل",        "Delivered"),
            }
            : new[]
            {
                (OrderStatus.Pending,         "تم استلام الطلب",    "Pending"),
                (OrderStatus.Confirmed,       "تم تأكيد الطلب",    "Confirmed"),
                (OrderStatus.Processing,      "جاري التحضير",      "Processing"),
                (OrderStatus.ReadyForPickup,  "جاهز للاستلام",     "ReadyForPickup"),
                (OrderStatus.Delivered,       "تم الاستلام",       "Delivered"),
            };

        var currentIdx = Array.FindIndex(steps, s => s.Item1 == order.Status);

        // لو ملغي أو مرتجع
        if (order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Returned)
        {
            return new List<object>
            {
                new { status = "Cancelled", label = order.Status == OrderStatus.Cancelled ? "تم الإلغاء" : "مرتجع", done = true, current = true, time = (DateTime?)null }
            };
        }

        return steps.Select((step, i) =>
        {
            var historyEntry = order.StatusHistory
                .FirstOrDefault(h => h.Status == step.Item1);

            return (object)new
            {
                status  = step.Item3,
                label   = step.Item2,
                done    = i <= currentIdx,
                current = i == currentIdx,
                time    = historyEntry?.CreatedAt ?? (i == 0 ? (DateTime?)order.CreatedAt : null),
            };
        }).ToList<object>();
    }

    // ── Helpers ───────────────────────────────────────────
    private static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("01") && digits.Length == 11) return digits;
        if (digits.StartsWith("201") && digits.Length == 12) return "0" + digits[2..];
        return digits;
    }

    private static string GetStatusAr(OrderStatus s) => s switch
    {
        OrderStatus.Pending        => "في الانتظار",
        OrderStatus.Confirmed      => "مؤكد",
        OrderStatus.Processing     => "جاري التحضير",
        OrderStatus.ReadyForPickup => "جاهز للاستلام",
        OrderStatus.OutForDelivery => "خرج للتوصيل",
        OrderStatus.Delivered      => "تم التوصيل",
        OrderStatus.Cancelled      => "ملغي",
        OrderStatus.Returned       => "مرتجع",
        _                          => s.ToString()
    };

    private static string GetStatusColor(OrderStatus s) => s switch
    {
        OrderStatus.Delivered      => "emerald",
        OrderStatus.Cancelled      => "red",
        OrderStatus.Returned       => "gray",
        OrderStatus.OutForDelivery => "blue",
        OrderStatus.ReadyForPickup => "purple",
        _                          => "amber",
    };

    private static string GetPaymentAr(PaymentMethod m) => m switch
    {
        PaymentMethod.Cash      => "كاش عند الاستلام",
        PaymentMethod.Vodafone  => "فودافون كاش",
        PaymentMethod.InstaPay  => "انستاباي",
        _                       => m.ToString()
    };

    private static string GetPaymentStatusAr(PaymentStatus p) => p switch
    {
        PaymentStatus.Paid     => "مدفوع",
        PaymentStatus.Pending  => "في الانتظار",
        PaymentStatus.Refunded => "مسترجع",
        PaymentStatus.Failed   => "فشل",
        _                      => p.ToString()
    };
}
