using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
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
    private readonly IOrderService _orderService;

    public BostaWebhookController(AppDbContext db, ILogger<BostaWebhookController> logger, IAuditService audit, IOrderService orderService)
    {
        _db = db;
        _logger = logger;
        _audit = audit;
        _orderService = orderService;
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
            
            try 
            {
                await _audit.LogAsync("BostaWebhookRaw", "Webhook", "0", payload.GetRawText(), null, "Bosta");
            } 
            catch { }

            string? trackingNumber = null;
            string? deliveryId = null;
            string? status = null;
            bool isConfirmedDelivery = false;

            if (payload.TryGetProperty("trackingNumber", out var trackingProp))
                trackingNumber = trackingProp.GetString();

            if (payload.TryGetProperty("_id", out var idProp))
                deliveryId = idProp.GetString();
            else if (payload.TryGetProperty("deliveryId", out var deliveryIdProp))
                deliveryId = deliveryIdProp.GetString();

            // Read isConfirmedDelivery flag
            if (payload.TryGetProperty("isConfirmedDelivery", out var confirmedProp) && confirmedProp.ValueKind == JsonValueKind.True)
                isConfirmedDelivery = true;

            // Read status - handle String, Number, and nested Object formats from Bosta
            if (payload.TryGetProperty("code", out var codeProp))
            {
                // Bosta sometimes sends status as numeric 'code' field
                if (codeProp.ValueKind == JsonValueKind.Number)
                    status = codeProp.GetInt32().ToString();
                else if (codeProp.ValueKind == JsonValueKind.String)
                    status = codeProp.GetString();
            }
            else if (payload.TryGetProperty("status", out var statusProp))
            {
                if (statusProp.ValueKind == JsonValueKind.String)
                    status = statusProp.GetString();
                else if (statusProp.ValueKind == JsonValueKind.Number)
                    status = statusProp.GetInt32().ToString();
                else if (statusProp.ValueKind == JsonValueKind.Object)
                {
                    if (statusProp.TryGetProperty("value", out var statusVal))
                        status = statusVal.ValueKind == JsonValueKind.Number ? statusVal.GetInt32().ToString() : statusVal.GetString();
                    else if (statusProp.TryGetProperty("code", out var statusCode))
                        status = statusCode.ValueKind == JsonValueKind.Number ? statusCode.GetInt32().ToString() : statusCode.GetString();
                }
            }
            else if (payload.TryGetProperty("state", out var stateProp))
            {
                if (stateProp.ValueKind == JsonValueKind.String)
                    status = stateProp.GetString();
                else if (stateProp.ValueKind == JsonValueKind.Number)
                    status = stateProp.GetInt32().ToString();
                else if (stateProp.ValueKind == JsonValueKind.Object && stateProp.TryGetProperty("value", out var stateVal))
                    status = stateVal.ValueKind == JsonValueKind.Number ? stateVal.GetInt32().ToString() : stateVal.GetString();
            }

            string? bType = null;
            if (payload.TryGetProperty("type", out var typeProp))
                bType = typeProp.GetString();

            string? bDesc = null;
            if (payload.TryGetProperty("description", out var descProp))
                bDesc = descProp.GetString();

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

            if (!string.IsNullOrEmpty(status) || isConfirmedDelivery || !string.IsNullOrEmpty(bDesc))
            {
                // Format friendly status text for tracking
                string statusLabel = !string.IsNullOrEmpty(bDesc) ? $"{status ?? ""} - {bDesc}".Trim(' ', '-') : (status ?? "");
                order.BostaShipmentStatus = !string.IsNullOrWhiteSpace(statusLabel) ? statusLabel : order.BostaShipmentStatus;

                var upperType = (bType ?? "").ToUpperInvariant();
                var upperDesc = (bDesc ?? "").ToUpperInvariant();
                var upperStatus = (status ?? "").ToUpperInvariant();

                // 🚫 1. RETURN TO SENDER / BUSINESS CHECK:
                // في بوسطة: كود 46 أو وصف "Returned to business" أو نوع RTO أو "تم التسليم" للمحل/الراسل
                bool isReturnToBusiness = upperStatus == "46" 
                    || upperType == "RTO" 
                    || upperType == "CUSTOMER_RETURN_PICKUP"
                    || upperDesc.Contains("RETURNED TO BUSINESS")
                    || upperDesc.Contains("DELIVERED TO SENDER")
                    || upperDesc.Contains("DELIVERED TO ORIGIN")
                    || upperDesc.Contains("RETURNED")
                    || (bDesc != null && (bDesc.Contains("تم التسليم للراسل") || bDesc.Contains("تم التسليم للتاجر") || bDesc.Contains("تم إرجاع الشحنة")));

                // ✅ 2. GENUINE CUSTOMER DELIVERY CHECK (تم بنجاح / تم التوصيل للعميل):
                // كود 45 أو confirmedDelivery=true (لنوع SEND فقط) أو وصف "Delivered" / "تم بنجاح"
                bool isDeliveredToCustomer = !isReturnToBusiness && (
                    upperStatus == "45" 
                    || upperStatus == "100" 
                    || (isConfirmedDelivery && upperType != "RTO")
                    || (upperDesc == "DELIVERED" && upperType == "SEND")
                    || (bDesc != null && (bDesc.Contains("تم بنجاح") || bDesc.Contains("تم التوصيل للعميل") || bDesc == "تم التوصيل"))
                );

                if (isDeliveredToCustomer)
                {
                    if (order.Status != OrderStatus.Delivered)
                    {
                        await _orderService.UpdateOrderStatusAsync(order.Id, new UpdateOrderStatusDto(OrderStatus.Delivered, $"Bosta Webhook (تسليم ناجح للعميل): code={status}, desc={bDesc}"), "BostaWebhook");
                    }
                }
                else
                {
                    // Just save the Bosta status update (Out for delivery, Exception, Return to store, etc.) without false delivery transitions
                    await _db.SaveChangesAsync();
                }

                try
                {
                    await _audit.LogAsync("BostaWebhook", "Order", order.Id.ToString(), $"Bosta Webhook updated order #{order.OrderNumber} status to {order.BostaShipmentStatus} (isDeliveredToCustomer={isDeliveredToCustomer})", null, "BostaWebhook");
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
