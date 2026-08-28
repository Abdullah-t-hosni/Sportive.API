using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;

namespace Sportive.API.Controllers.Webhooks;

[ApiController]
[Route("api/webhooks/whatsapp")]
[Route("api/whatsapp/webhook")]
[AllowAnonymous]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notificationService;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        AppDbContext db,
        INotificationService notificationService,
        ILogger<WhatsAppWebhookController> logger)
    {
        _db = db;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// Verification endpoint for Webhook Setup
    /// GET /api/webhooks/whatsapp
    /// </summary>
    [HttpGet]
    public IActionResult VerifyWebhook([FromQuery(Name = "hub.challenge")] string? challenge)
    {
        if (!string.IsNullOrEmpty(challenge))
        {
            return Ok(challenge);
        }
        return Ok(new { status = "WhatsApp Webhook Listener Active", timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Primary Webhook Handler for incoming WhatsApp messages from Gateway / Baileys / Wapilot
    /// POST /api/webhooks/whatsapp
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> HandleIncomingMessage([FromBody] JsonElement payload)
    {
        try
        {
            _logger.LogInformation("Received WhatsApp Webhook Payload: {Payload}", payload.GetRawText());

            string? phone = null;
            string? customerName = null;
            string? messageText = null;
            bool fromMe = false;

            // 1. Direct format: { phone, customerName, message, fromMe }
            if (payload.TryGetProperty("phone", out var phoneProp))
                phone = phoneProp.GetString();
            else if (payload.TryGetProperty("from", out var fromProp))
                phone = fromProp.GetString();
            else if (payload.TryGetProperty("sender", out var senderProp))
                phone = senderProp.GetString();

            if (payload.TryGetProperty("customerName", out var nameProp))
                customerName = nameProp.GetString();
            else if (payload.TryGetProperty("name", out var nProp))
                customerName = nProp.GetString();
            else if (payload.TryGetProperty("pushName", out var pnProp))
                customerName = pnProp.GetString();

            if (payload.TryGetProperty("message", out var msgProp))
            {
                if (msgProp.ValueKind == JsonValueKind.String)
                    messageText = msgProp.GetString();
                else if (msgProp.ValueKind == JsonValueKind.Object)
                {
                    if (msgProp.TryGetProperty("conversation", out var convProp))
                        messageText = convProp.GetString();
                    else if (msgProp.TryGetProperty("text", out var textProp))
                        messageText = textProp.GetString();
                    else if (msgProp.TryGetProperty("caption", out var capProp))
                        messageText = capProp.GetString();
                }
            }
            else if (payload.TryGetProperty("text", out var tProp))
                messageText = tProp.GetString();
            else if (payload.TryGetProperty("body", out var bProp))
                messageText = bProp.GetString();

            if (payload.TryGetProperty("fromMe", out var fromMeProp))
                fromMe = fromMeProp.GetBoolean();

            // 2. Baileys / Nested format: { event: "messages.upsert", data: { key: { remoteJid, fromMe }, message: { ... } } }
            if (payload.TryGetProperty("data", out var dataProp))
            {
                if (dataProp.TryGetProperty("key", out var keyProp))
                {
                    if (keyProp.TryGetProperty("remoteJid", out var jidProp) && string.IsNullOrEmpty(phone))
                        phone = jidProp.GetString();
                    if (keyProp.TryGetProperty("fromMe", out var dataFromMeProp))
                        fromMe = dataFromMeProp.GetBoolean();
                }
                if (dataProp.TryGetProperty("pushName", out var dNameProp) && string.IsNullOrEmpty(customerName))
                    customerName = dNameProp.GetString();
                if (dataProp.TryGetProperty("message", out var dMsgProp) && string.IsNullOrEmpty(messageText))
                {
                    if (dMsgProp.TryGetProperty("conversation", out var cProp))
                        messageText = cProp.GetString();
                    else if (dMsgProp.TryGetProperty("text", out var tProp2))
                        messageText = tProp2.GetString();
                    else if (dMsgProp.TryGetProperty("imageMessage", out var imgProp) && imgProp.TryGetProperty("caption", out var imgCap))
                        messageText = "📷 " + (imgCap.GetString() ?? "صورة");
                    else if (dMsgProp.TryGetProperty("audioMessage", out _))
                        messageText = "🎤 رسالة صوتية";
                    else if (dMsgProp.TryGetProperty("documentMessage", out var docProp) && docProp.TryGetProperty("fileName", out var fnProp))
                        messageText = "📄 ملف: " + fnProp.GetString();
                }
            }

            // Do not notify admins about outgoing messages sent by the store bot/staff
            if (fromMe)
            {
                return Ok(new { status = "ignored", reason = "Message is from store bot/staff" });
            }

            if (string.IsNullOrWhiteSpace(phone) && string.IsNullOrWhiteSpace(messageText))
            {
                return BadRequest(new { status = "error", message = "Missing phone number and message content" });
            }

            // Clean up phone number (remove @s.whatsapp.net, +, spaces)
            var cleanPhone = Regex.Replace(phone ?? string.Empty, @"[@a-zA-Z\.\+_\s-]", "");
            if (cleanPhone.StartsWith("20") && cleanPhone.Length == 12)
                cleanPhone = "0" + cleanPhone.Substring(2);

            // Look up customer in database by phone or hash
            Customer? customer = null;
            int? customerId = null;

            if (!string.IsNullOrEmpty(cleanPhone))
            {
                var phoneHash = Customer.EncryptionHelper?.ComputeSearchHash(cleanPhone) ?? cleanPhone;
                customer = await _db.Customers
                    .FirstOrDefaultAsync(c => c.PhoneHash == phoneHash || c.Phone == cleanPhone);

                if (customer != null)
                {
                    customerId = customer.Id;
                    if (string.IsNullOrEmpty(customerName) || customerName.ToLower().Contains("guest") || customerName.ToLower().Contains("زائر"))
                    {
                        customerName = customer.FullName;
                    }
                }
            }

            var displayName = !string.IsNullOrWhiteSpace(customerName) ? customerName : (!string.IsNullOrWhiteSpace(cleanPhone) ? cleanPhone : "عميل واتساب");
            var displayMsg = !string.IsNullOrWhiteSpace(messageText) ? messageText : "رسالة جديدة عبر الواتساب";

            var titleAr = $"💬 رسالة واتساب: {displayName} ({cleanPhone})";
            var titleEn = $"💬 WhatsApp from {displayName} ({cleanPhone})";

            // Broadcast notification to all staff who have WhatsApp preferences enabled
            await _notificationService.SendAsync(
                userId: null,
                titleAr: titleAr,
                titleEn: titleEn,
                msgAr: displayMsg,
                msgEn: displayMsg,
                type: "WhatsApp",
                orderId: customerId
            );

            _logger.LogInformation("Dispatched WhatsApp notification for customer {Name} ({Phone})", displayName, cleanPhone);

            return Ok(new
            {
                status = "success",
                notified = true,
                customer = displayName,
                phone = cleanPhone,
                message = displayMsg
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WhatsApp webhook");
            return StatusCode(500, new { status = "error", message = ex.Message });
        }
    }

    /// <summary>
    /// Test endpoint to trigger a WhatsApp incoming message notification
    /// POST /api/webhooks/whatsapp/test
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> TestWhatsAppNotification([FromBody] TestNotificationRequest request)
    {
        var cleanPhone = Regex.Replace(request.Phone ?? "01000000000", @"[@a-zA-Z\.\+_\s-]", "");
        var customerName = request.CustomerName ?? "عميل تجريبي";
        var message = request.Message ?? "السلام عليكم، هل المنتج متاح؟";

        var titleAr = $"💬 رسالة واتساب: {customerName} ({cleanPhone})";
        var titleEn = $"💬 WhatsApp from {customerName} ({cleanPhone})";

        await _notificationService.SendAsync(
            userId: null,
            titleAr: titleAr,
            titleEn: titleEn,
            msgAr: message,
            msgEn: message,
            type: "WhatsApp",
            orderId: null
        );

        return Ok(new
        {
            success = true,
            message = "WhatsApp notification test dispatched successfully!",
            phone = cleanPhone,
            customer = customerName
        });
    }

    public class TestNotificationRequest
    {
        public string? Phone { get; set; }
        public string? CustomerName { get; set; }
        public string? Message { get; set; }
    }
}
