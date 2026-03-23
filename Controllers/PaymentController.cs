using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentController : ControllerBase
{
    private readonly IPaymobService _paymob;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<PaymentController> _logger;

    public PaymentController(IPaymobService paymob, AppDbContext db, ILogger<PaymentController> logger, IConfiguration config)
    {
        _paymob = paymob;
        _db     = db;
        _logger = logger;
        _config = config;
    }

    /// <summary>إنشاء رابط دفع Paymob لطلب معين</summary>
    [Authorize]
    [HttpPost("create/{orderId}")]
    public async Task<IActionResult> CreatePayment(int orderId)
    {
        var order = await _db.Orders.FindAsync(orderId);
        if (order == null)
            return NotFound(new { message = "الطلب غير موجود" });

        if (order.PaymentStatus == PaymentStatus.Paid)
            return BadRequest(new { message = "الطلب مدفوع مسبقاً" });

        var result = await _paymob.CreatePaymentAsync(new PaymobOrderRequest(
            Amount: order.TotalAmount,
            OrderId: order.Id,
            OrderNumber: order.OrderNumber
        ));

        if (!result.Success)
            return BadRequest(new { message = result.Error ?? "فشل إنشاء رابط الدفع" });

        return Ok(new { paymentUrl = result.PaymentUrl, orderNumber = order.OrderNumber });
    }

    /// <summary>Paymob Callback — يستقبل نتيجة الدفع</summary>
    [HttpPost("callback")]
    public async Task<IActionResult> Callback([FromForm] Dictionary<string, string> data)
    {
        _logger.LogInformation("Paymob callback received");

        if (!_paymob.VerifyCallback(data))
        {
            _logger.LogWarning("Paymob HMAC verification failed");
            return BadRequest(new { message = "Invalid HMAC" });
        }

        var success  = data.GetValueOrDefault("success")?.ToLower() == "true";
        var orderRef = data.GetValueOrDefault("merchant_order_id");

        if (!string.IsNullOrEmpty(orderRef))
        {
            var order = await _db.Orders.FirstOrDefaultAsync(o => o.OrderNumber == orderRef);
            if (order != null)
            {
                order.PaymentStatus = success ? PaymentStatus.Paid : PaymentStatus.Failed;
                if (success && order.Status == OrderStatus.Pending)
                    order.Status = OrderStatus.Confirmed;
                order.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                _logger.LogInformation("Order {OrderNumber} payment {Status}", orderRef, success ? "PAID" : "FAILED");
            }
        }

        return Ok();
    }

    /// <summary>Paymob Redirect بعد الدفع</summary>
    [HttpGet("result")]
    public IActionResult PaymentResult([FromQuery] string? success, [FromQuery] string? merchant_order_id)
    {
        var frontendUrl = _config["AllowedOrigins"]?.Split(';').FirstOrDefault() 
            ?? "http://localhost:5173";

        var redirectUrl = success?.ToLower() == "true"
            ? $"{frontendUrl}/orders?payment=success&order={merchant_order_id}"
            : $"{frontendUrl}/orders?payment=failed&order={merchant_order_id}";

        return Redirect(redirectUrl);
    }
}
