using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Sportive.API.Utils;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
public class PaymentController : ControllerBase
{
    private readonly IPaymobService _paymob;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<PaymentController> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAuditService _audit;
    private readonly ITranslator _t;

    public PaymentController(IPaymobService paymob, AppDbContext db, ILogger<PaymentController> logger, IConfiguration config, IServiceScopeFactory scopeFactory, IAuditService audit, ITranslator t)
    {
        _paymob = paymob;
        _db = db;
        _logger = logger;
        _config = config;
        _scopeFactory = scopeFactory;
        _audit = audit;
        _t = t;
    }

    /// <summary>إنشاء رابط دفع Paymob لطلب معين</summary>
    [Authorize]
    [HttpPost("create/{orderId}")]
    public async Task<IActionResult> CreatePayment(int orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
            return NotFound(new { message = _t.Get("Payments.OrderNotFound") });

        if (order.PaymentStatus == PaymentStatus.Paid)
            return BadRequest(new { message = _t.Get("Payments.AlreadyPaid") });

        var result = await _paymob.CreatePaymentAsync(new PaymobOrderRequest(
            Amount: order.TotalAmount,
            OrderId: order.Id,
            OrderNumber: order.OrderNumber,
            Email: order.Customer.Email,
            Phone: order.Customer.Phone ?? "01000000000",
            FullName: order.Customer.FullName
        ));

        if (!result.Success)
            return BadRequest(new { message = result.Error ?? _t.Get("Payments.LinkCreationFailed") });

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
                if (order.PaymentStatus == PaymentStatus.Paid && success)
                    return Ok(); // Already processed

                order.PaymentStatus = success ? PaymentStatus.Paid : PaymentStatus.Failed;
                if (success && order.Status == OrderStatus.Pending)
                    order.Status = OrderStatus.Confirmed;
                order.UpdatedAt = TimeHelper.GetEgyptTime();
                await _db.SaveChangesAsync();
                _logger.LogInformation("Order {OrderNumber} payment {Status}", orderRef, success ? "PAID" : "FAILED");

                if (success)
                    BackgroundJob.Enqueue<IAccountingService>(a => a.PostOrderPaymentByIdAsync(order.Id));
            }
        }

        return Ok();
    }

    /// <summary>Paymob Webhook — يستقبل نتيجة الدفع كـ JSON (أكثر أماناً)</summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromQuery] string hmac, [FromBody] JsonElement payload)
    {
        _logger.LogInformation("Paymob Transaction Webhook received");
        
        if (!_paymob.VerifyWebhook(payload, hmac))
        {
            _logger.LogWarning("Paymob HMAC verification failed on Webhook");
            return BadRequest(new { message = "Invalid HMAC signature" });
        }
        
        try {
            var obj         = payload.GetProperty("obj");
            var success     = obj.GetProperty("success").GetBoolean();
            var orderRef    = obj.GetProperty("order").GetProperty("merchant_order_id").GetString();
            
            if (!string.IsNullOrEmpty(orderRef)) {
                var order = await _db.Orders.FirstOrDefaultAsync(o => o.OrderNumber == orderRef);
                if (order != null) {
                    if (order.PaymentStatus == PaymentStatus.Paid && success)
                        return Ok();

                    order.PaymentStatus = success ? PaymentStatus.Paid : PaymentStatus.Failed;
                    if (success && order.Status == OrderStatus.Pending)
                        order.Status = OrderStatus.Confirmed;
                    order.UpdatedAt = TimeHelper.GetEgyptTime();
                    await _db.SaveChangesAsync();
                    
                    await _audit.LogAsync(success ? "PaymentSuccess" : "PaymentFailed", "Order", order.Id.ToString(), $"Webhook payment response", null, "PaymobWebhook");

                    if (success)
                        BackgroundJob.Enqueue<IAccountingService>(a => a.PostOrderPaymentByIdAsync(order.Id));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Paymob Webhook");
        }
        return Ok();
    }

    /// <summary>Paymob Redirect بعد الدفع</summary>
    [HttpGet("result")]
    public async Task<IActionResult> PaymentResult([FromQuery] string? success, [FromQuery] string? merchant_order_id)
    {
        var frontendUrl = _config["AllowedOrigins"]?.Split(';').FirstOrDefault() 
            ?? "http://localhost:5173";

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.OrderNumber == merchant_order_id);
        
        if (order == null)
            return Redirect($"{frontendUrl}/orders");

        var redirectUrl = success?.ToLower() == "true"
            ? $"{frontendUrl}/order-success/{order.Id}?payment=success"
            : $"{frontendUrl}/order-success/{order.Id}?payment=failed";

        return Redirect(redirectUrl);
    }
}
