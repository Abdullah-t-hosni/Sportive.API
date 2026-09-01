using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Hubs;
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
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        AppDbContext db,
        INotificationService notificationService,
        IHubContext<NotificationHub> hubContext,
        ILogger<WhatsAppWebhookController> logger)
    {
        _db = db;
        _notificationService = notificationService;
        _hubContext = hubContext;
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

            string? phone = ExtractPhone(payload);
            string? customerName = ExtractCustomerName(payload);
            string? messageText = ExtractMessageText(payload);
            bool fromMe = ExtractFromMe(payload);

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

            var chatLink = $"/admin/store-management?tab=orders&chatPhone={cleanPhone}&customerName={Uri.EscapeDataString(displayName)}&openChat=true";

            // Only send staff audio/toast notifications for INCOMING customer messages (fromMe == false)
            if (!fromMe)
            {
                await _notificationService.SendAsync(
                    userId: null,
                    titleAr: titleAr,
                    titleEn: titleEn,
                    msgAr: displayMsg,
                    msgEn: displayMsg,
                    type: "WhatsApp",
                    orderId: customerId
                );
            }

            // Broadcast real-time WhatsApp message event to connected SignalR clients (both incoming & outgoing)
            await _hubContext.Clients.All.SendAsync("ReceiveWhatsAppMessage", new
            {
                phone = cleanPhone,
                customerName = displayName,
                text = displayMsg,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                fromMe = fromMe,
                link = chatLink
            });

            _logger.LogInformation("Dispatched WhatsApp SignalR message for {Name} ({Phone}), fromMe={FromMe}", displayName, cleanPhone, fromMe);

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

    private static string? GetPropCaseInsensitive(JsonElement elem, params string[] names)
    {
        if (elem.ValueKind != JsonValueKind.Object) return null;

        foreach (var prop in elem.EnumerateObject())
        {
            foreach (var target in names)
            {
                if (string.Equals(prop.Name, target, StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        return prop.Value.GetString();
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                        return prop.Value.GetRawText();
                }
            }
        }
        return null;
    }

    private static string? ExtractPhone(JsonElement payload)
    {
        var p = GetPropCaseInsensitive(payload, "phone", "from", "sender", "remoteJid", "jid");
        if (!string.IsNullOrEmpty(p)) return p;

        if (payload.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.Object)
            {
                var jid = GetPropCaseInsensitive(key, "remoteJid", "participant", "from");
                if (!string.IsNullOrEmpty(jid)) return jid;
            }
            var dPhone = GetPropCaseInsensitive(data, "phone", "from", "sender", "remoteJid");
            if (!string.IsNullOrEmpty(dPhone)) return dPhone;
        }
        return null;
    }

    private static string? ExtractCustomerName(JsonElement payload)
    {
        var n = GetPropCaseInsensitive(payload, "customerName", "name", "pushName", "author");
        if (!string.IsNullOrEmpty(n)) return n;

        if (payload.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            var dn = GetPropCaseInsensitive(data, "pushName", "customerName", "name", "verifiedBizName");
            if (!string.IsNullOrEmpty(dn)) return dn;
        }
        return null;
    }

    private static string? ExtractMessageText(JsonElement payload)
    {
        var direct = GetPropCaseInsensitive(payload, "message", "text", "body", "conversation", "caption");
        if (!string.IsNullOrEmpty(direct)) return direct;

        if (payload.TryGetProperty("message", out var msgObj) && msgObj.ValueKind == JsonValueKind.Object)
        {
            var subMsg = GetPropCaseInsensitive(msgObj, "conversation", "text", "caption");
            if (!string.IsNullOrEmpty(subMsg)) return subMsg;
        }

        if (payload.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            var dMsg = ExtractMessageText(data);
            if (!string.IsNullOrEmpty(dMsg)) return dMsg;
        }

        return null;
    }

    private static bool ExtractFromMe(JsonElement payload)
    {
        if (payload.TryGetProperty("fromMe", out var f1))
        {
            if (f1.ValueKind == JsonValueKind.True) return true;
            if (f1.ValueKind == JsonValueKind.String && bool.TryParse(f1.GetString(), out var b1)) return b1;
        }

        if (payload.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.Object)
            {
                if (key.TryGetProperty("fromMe", out var f2))
                {
                    if (f2.ValueKind == JsonValueKind.True) return true;
                    if (f2.ValueKind == JsonValueKind.String && bool.TryParse(f2.GetString(), out var b2)) return b2;
                }
            }
        }
        return false;
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
        var chatLink = $"/admin/store-management?tab=orders&chatPhone={cleanPhone}&customerName={Uri.EscapeDataString(customerName)}&openChat=true";

        await _notificationService.SendAsync(
            userId: null,
            titleAr: titleAr,
            titleEn: titleEn,
            msgAr: message,
            msgEn: message,
            type: "WhatsApp",
            orderId: null
        );

        await _hubContext.Clients.All.SendAsync("ReceiveWhatsAppMessage", new
        {
            phone = cleanPhone,
            customerName = customerName,
            text = message,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            fromMe = false,
            link = chatLink
        });

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
