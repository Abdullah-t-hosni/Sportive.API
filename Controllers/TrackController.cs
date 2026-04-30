using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

/// <summary>
/// GET /api/track?orderNumber=SZ-xxx&amp;phone=01xxxxxxxx
/// Public endpoint — no login required.
/// Returns order status if order number + phone match.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TrackController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITranslator _t;

    public TrackController(AppDbContext db, ITranslator t)
    {
        _db = db;
        _t = t;
    }

    [HttpGet]
    public async Task<IActionResult> Track(
        [FromQuery] string orderNumber,
        [FromQuery] string phone)
    {
        if (string.IsNullOrWhiteSpace(orderNumber) || string.IsNullOrWhiteSpace(phone))
            return BadRequest(new { message = _t.Get("Track.OrderPhoneRequired") });

        var normalizedPhone = NormalizePhone(phone.Trim());
        var normalizedOrder = orderNumber.Trim().ToUpper();

        var order = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p!.Images.Where(img => img.IsMain))
            .Include(o => o.DeliveryAddress)
            .Include(o => o.StatusHistory.OrderByDescending(h => h.CreatedAt))
            .Where(o => o.OrderNumber.ToUpper() == normalizedOrder)
            .FirstOrDefaultAsync();

        if (order == null)
            return NotFound(new { message = _t.Get("Track.OrderNotFound") });

        var customerPhone = NormalizePhone(order.Customer?.Phone ?? "");
        if (customerPhone != normalizedPhone)
            return NotFound(new { message = _t.Get("Track.InvalidOrderOrPhone") });

        var timeline = BuildTimeline(order);

        return Ok(new
        {
            orderNumber  = order.OrderNumber,
            status       = order.Status.ToString(),
            statusAr     = GetStatusAr(order.Status),
            statusColor  = GetStatusColor(order.Status),
            isActive     = order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Returned,
            fulfillment  = order.FulfillmentType.ToString(),
            fulfillmentAr= order.FulfillmentType == FulfillmentType.Delivery
                ? _t.Get("WhatsApp.Fulfillment.Delivery")
                : _t.Get("WhatsApp.Fulfillment.Pickup"),
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
            attachmentUrl = order.AttachmentUrl,
        });
    }

    /// <summary>
    /// GET /api/track/by-phone?phone=01xxxxxxxx
    /// Returns a list of in-progress orders for this phone number.
    /// </summary>
    [HttpGet("by-phone")]
    public async Task<IActionResult> TrackByPhone([FromQuery] string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return BadRequest(new { message = _t.Get("Track.PhoneRequired") });

        var normalizedPhone = NormalizePhone(phone.Trim());
        // Match on last 10 digits to handle different country codes
        var searchSuffix = normalizedPhone.Length >= 10
            ? normalizedPhone.Substring(normalizedPhone.Length - 10)
            : normalizedPhone;

        var orders = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p!.Images.Where(img => img.IsMain))
            .Include(o => o.DeliveryAddress)
            .Include(o => o.StatusHistory.OrderByDescending(h => h.CreatedAt))
            .Where(o => o.Customer != null && o.Customer.Phone != null && o.Customer.Phone.EndsWith(searchSuffix)
                     && o.Status != OrderStatus.Delivered
                     && o.Status != OrderStatus.Cancelled
                     && o.Status != OrderStatus.Returned)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        if (!orders.Any())
            return NotFound(new { message = _t.Get("Track.NoActiveOrders") });

        return Ok(orders.Select(order => new
        {
            id           = order.Id,
            orderNumber  = order.OrderNumber,
            status       = order.Status.ToString(),
            statusAr     = GetStatusAr(order.Status),
            statusColor  = GetStatusColor(order.Status),
            isActive     = true,
            fulfillment  = order.FulfillmentType.ToString(),
            fulfillmentAr= order.FulfillmentType == FulfillmentType.Delivery
                ? _t.Get("WhatsApp.Fulfillment.Delivery")
                : _t.Get("WhatsApp.Fulfillment.Pickup"),
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
            timeline = BuildTimeline(order),
            customerName = order.Customer?.FullName,
            attachmentUrl = order.AttachmentUrl,
        }));
    }

    // ── Timeline ─────────────────────────────────────────
    private List<object> BuildTimeline(Order order)
    {
        var steps = order.FulfillmentType == FulfillmentType.Delivery
            ? new[]
            {
                (OrderStatus.Pending,        _t.Get("Status.Pending"),        "Pending"),
                (OrderStatus.Confirmed,      _t.Get("Status.Confirmed"),      "Confirmed"),
                (OrderStatus.Processing,     _t.Get("Status.Processing"),     "Processing"),
                (OrderStatus.OutForDelivery, _t.Get("Status.OutForDelivery"), "OutForDelivery"),
                (OrderStatus.Delivered,      _t.Get("Status.Delivered"),      "Delivered"),
            }
            : new[]
            {
                (OrderStatus.Pending,         _t.Get("Status.Pending"),          "Pending"),
                (OrderStatus.Confirmed,       _t.Get("Status.Confirmed"),        "Confirmed"),
                (OrderStatus.Processing,      _t.Get("Status.Processing"),       "Processing"),
                (OrderStatus.ReadyForPickup,  _t.Get("Status.ReadyForPickup"),   "ReadyForPickup"),
                (OrderStatus.Delivered,       _t.Get("Status.DeliveredPickup"),  "Delivered"),
            };

        var currentIdx = Array.FindIndex(steps, s => s.Item1 == order.Status);

        if (order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Returned)
        {
            var label = order.Status == OrderStatus.Cancelled
                ? _t.Get("Status.Cancelled")
                : _t.Get("Status.Returned");

            return new List<object>
            {
                new { status = "Cancelled", label, done = true, current = true, time = (DateTime?)null }
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

    private string GetStatusAr(OrderStatus s) => s switch
    {
        OrderStatus.Pending        => _t.Get("Status.Waiting"),
        OrderStatus.Confirmed      => _t.Get("Status.ConfirmedShort"),
        OrderStatus.Processing     => _t.Get("Status.ProcessingShort"),
        OrderStatus.ReadyForPickup => _t.Get("Status.ReadyForPickupShort"),
        OrderStatus.OutForDelivery => _t.Get("Status.OutForDeliveryShort"),
        OrderStatus.Delivered      => _t.Get("Status.DeliveredShort"),
        OrderStatus.Cancelled      => _t.Get("Status.Cancelled"),
        OrderStatus.Returned       => _t.Get("Status.Returned"),
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

    private string GetPaymentAr(PaymentMethod m) => m switch
    {
        PaymentMethod.Cash      => _t.Get("WhatsApp.Payment.Cash"),
        PaymentMethod.Vodafone  => _t.Get("WhatsApp.Payment.Vodafone"),
        PaymentMethod.InstaPay  => _t.Get("WhatsApp.Payment.InstaPay"),
        _                       => m.ToString()
    };

    private string GetPaymentStatusAr(PaymentStatus p) => p switch
    {
        PaymentStatus.Paid     => _t.Get("PayStatus.Paid"),
        PaymentStatus.Pending  => _t.Get("PayStatus.Pending"),
        PaymentStatus.Refunded => _t.Get("PayStatus.Refunded"),
        PaymentStatus.Failed   => _t.Get("PayStatus.Failed"),
        _                      => p.ToString()
    };
}
