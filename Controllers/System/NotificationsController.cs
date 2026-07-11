using Sportive.API.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config;
    
    public NotificationsController(AppDbContext db, Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    private string GetUserId() => User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;

    /// <summary>GET /api/notifications — كل الإشعارات</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var userId = GetUserId();
        var notifications = await _db.Set<Notification>()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new {
                n.Id,
                n.TitleAr,
                n.TitleEn,
                n.MessageAr,
                n.MessageEn,
                n.Type,
                n.IsRead,
                n.OrderId,
                n.CreatedAt
            })
            .ToListAsync();

        return Ok(notifications);
    }

    /// <summary>GET /api/notifications/unread-count</summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var userId = GetUserId();
        var count = await _db.Set<Notification>()
            .CountAsync(n => n.UserId == userId && !n.IsRead);
        return Ok(count);
    }

    /// <summary>PATCH /api/notifications/{id}/read</summary>
    [HttpPatch("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var userId = GetUserId();
        var n = await _db.Set<Notification>()
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

        if (n == null) return NotFound();

        n.IsRead    = true;
        n.UpdatedAt = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>PATCH /api/notifications/read-all</summary>
    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var userId = GetUserId();
        var notifications = await _db.Set<Notification>()
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        foreach (var n in notifications)
        {
            n.IsRead    = true;
            n.UpdatedAt = TimeHelper.GetEgyptTime();
        }

        await _db.SaveChangesAsync();
        return Ok(new { updated = notifications.Count });
    }

    /// <summary>DELETE /api/notifications/{id} — حذف إشعار</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var n = await _db.Set<Notification>()
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (n == null) return NotFound();
        _db.Set<Notification>().Remove(n);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>DELETE /api/notifications/clear-all — مسح كل الإشعارات</summary>
    [HttpDelete("clear-all")]
    public async Task<IActionResult> ClearAll()
    {
        var userId = GetUserId();
        var notifications = await _db.Set<Notification>()
            .Where(n => n.UserId == userId)
            .ToListAsync();
        _db.Set<Notification>().RemoveRange(notifications);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = notifications.Count });
    }

    public class PushSubscriptionDto
    {
        public string Endpoint { get; set; } = string.Empty;
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;
    }

    /// <summary>POST /api/notifications/subscribe — تسجيل جهاز جديد لاستقبال الإشعارات</summary>
    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] PushSubscriptionDto dto)
    {
        var userId = GetUserId();
        
        // Check if subscription already exists
        var existing = await _db.Set<PushSubscription>()
            .FirstOrDefaultAsync(s => s.Endpoint == dto.Endpoint && s.UserId == userId);

        if (existing == null)
        {
            var sub = new PushSubscription
            {
                UserId = userId,
                Endpoint = dto.Endpoint,
                P256dh = dto.P256dh,
                Auth = dto.Auth
            };
            _db.Set<PushSubscription>().Add(sub);
            await _db.SaveChangesAsync();
        }

        return Ok(new { success = true });
    }

    /// <summary>POST /api/notifications/unsubscribe — إلغاء التسجيل</summary>
    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromBody] PushSubscriptionDto dto)
    {
        var userId = GetUserId();
        
        var existing = await _db.Set<PushSubscription>()
            .FirstOrDefaultAsync(s => s.Endpoint == dto.Endpoint && s.UserId == userId);

        if (existing != null)
        {
            _db.Set<PushSubscription>().Remove(existing);
            await _db.SaveChangesAsync();
        }

        return Ok(new { success = true });
    }
    /// <summary>GET /api/notifications/test-push — أداة تشخيص الإشعارات</summary>
    [HttpGet("test-push")]
    public async Task<IActionResult> TestPush()
    {
        var userId = GetUserId();
        var subscriptions = await _db.Set<PushSubscription>().Where(s => s.UserId == userId).ToListAsync();
        if (!subscriptions.Any()) return BadRequest(new { error = "No push subscriptions found for this user. Did you click the bell icon?" });

        var subject = _config["Vapid:Subject"];
        var publicKey = _config["Vapid:PublicKey"];
        var privateKey = _config["Vapid:PrivateKey"];
        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(privateKey))
            return BadRequest(new { error = "VAPID keys are missing from configuration." });

        var vapidDetails = new WebPush.VapidDetails(subject, publicKey, privateKey);
        var webPushClient = new WebPush.WebPushClient();
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            titleAr = "إشعار تجريبي",
            titleEn = "Test Notification",
            msgAr = "هذا إشعار للتأكد من وصول الخدمة لجهازك.",
            msgEn = "This is a test notification to verify delivery.",
            type = "System",
            orderId = (int?)null
        });

        var results = new List<object>();
        foreach (var sub in subscriptions)
        {
            try
            {
                var pushSubscription = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                await webPushClient.SendNotificationAsync(pushSubscription, payload, vapidDetails);
                results.Add(new { endpoint = sub.Endpoint, success = true });
            }
            catch (WebPush.WebPushException ex)
            {
                results.Add(new { endpoint = sub.Endpoint, success = false, error = ex.Message, statusCode = ex.StatusCode });
            }
            catch (Exception ex)
            {
                results.Add(new { endpoint = sub.Endpoint, success = false, error = ex.Message });
            }
        }
        return Ok(new { success = true, subCount = subscriptions.Count, results });
    }
}
