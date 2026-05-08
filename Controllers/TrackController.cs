using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
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
    [Authorize]
    public async Task<IActionResult> Track()
    {
        // This endpoint is now redundant as /api/orders/my handles this.
        // But we could implement a version that requires authentication if needed.
        return BadRequest(new { message = "Please use /api/orders/my to track your orders." });
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
