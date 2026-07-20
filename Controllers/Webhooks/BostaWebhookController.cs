using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;

namespace Sportive.API.Controllers.Webhooks;

[ApiController]
[Route("api/webhooks/bosta")]
[AllowAnonymous]
public class BostaWebhookController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<BostaWebhookController> _logger;
    private readonly IAuditService _audit;

    public BostaWebhookController(AppDbContext db, ILogger<BostaWebhookController> logger, IAuditService audit)
    {
        _db = db;
        _logger = logger;
        _audit = audit;
    }

    /// <summary>
    /// استقبال تحديثات حالة الشحنات من شركة بوسطة تلقائياً (Bosta Webhook Listener)
    /// POST /api/webhooks/bosta
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> HandleBostaWebhook([FromBody] JsonElement payload)
    {
        try
        {
            _logger.LogInformation("Received Bosta Webhook payload: {Payload}", payload.GetRawText());

            string? trackingNumber = null;
            string? deliveryId = null;
            string? status = null;

            if (payload.TryGetProperty("trackingNumber", out var trackingProp))
                trackingNumber = trackingProp.GetString();

            if (payload.TryGetProperty("_id", out var idProp))
                deliveryId = idProp.GetString();
            else if (payload.TryGetProperty("deliveryId", out var deliveryIdProp))
                deliveryId = deliveryIdProp.GetString();

            if (payload.TryGetProperty("status", out var statusProp))
            {
                if (statusProp.ValueKind == JsonValueKind.String)
                    status = statusProp.GetString();
                else if (statusProp.ValueKind == JsonValueKind.Object && statusProp.TryGetProperty("value", out var statusVal))
                    status = statusVal.GetString();
            }
            else if (payload.TryGetProperty("state", out var stateProp))
            {
                if (stateProp.ValueKind == JsonValueKind.String)
                    status = stateProp.GetString();
                else if (stateProp.ValueKind == JsonValueKind.Object && stateProp.TryGetProperty("value", out var stateVal))
                    status = stateVal.GetString();
            }

            if (string.IsNullOrEmpty(trackingNumber) && string.IsNullOrEmpty(deliveryId))
            {
                _logger.LogWarning("Bosta Webhook missing tracking number and delivery ID.");
                return Ok(new { success = false, message = "Missing tracking identifier" });
            }

            var order = await _db.Orders.FirstOrDefaultAsync(o => 
                (!string.IsNullOrEmpty(trackingNumber) && o.BostaTrackingNumber == trackingNumber) ||
                (!string.IsNullOrEmpty(deliveryId) && o.BostaDeliveryId == deliveryId));

            if (order == null)
            {
                _logger.LogWarning("Bosta Webhook: Order not found for trackingNumber={TrackingNumber}, deliveryId={DeliveryId}", trackingNumber, deliveryId);
                return Ok(new { success = false, message = "Order not found" });
            }

            if (!string.IsNullOrEmpty(status))
            {
                order.BostaShipmentStatus = status;
                
                var upperStatus = status.ToUpperInvariant();
                if (upperStatus.Contains("DELIVERED") || upperStatus == "100" || upperStatus == "45")
                {
                    order.Status = OrderStatus.Delivered;
                    order.PaymentStatus = PaymentStatus.Paid;
                }
                else if (upperStatus.Contains("CANCEL") || upperStatus.Contains("RETURN") || upperStatus.Contains("TERMINAT"))
                {
                    order.Status = OrderStatus.Cancelled;
                }
                else if (upperStatus.Contains("DELIVERY") || upperStatus.Contains("TRANSIT") || upperStatus.Contains("PICKED"))
                {
                    order.Status = OrderStatus.OutForDelivery;
                }

                await _db.SaveChangesAsync();

                try
                {
                    await _audit.LogAsync("BostaWebhook", "Order", order.Id.ToString(), $"Bosta Webhook updated order #{order.OrderNumber} status to {status}", null, "BostaWebhook");
                }
                catch { }
            }

            return Ok(new { success = true, orderId = order.Id, trackingNumber = order.BostaTrackingNumber, status = order.BostaShipmentStatus });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Bosta Webhook");
            return Ok(new { success = false, error = ex.Message }); // Always return 200 OK to acknowledge receipt
        }
    }
}
