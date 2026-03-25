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
    public NotificationsController(AppDbContext db) => _db = db;

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>GET /api/notifications — كل الإشعارات</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var userId = GetUserId();
        var notifications = await _db.Set<Notification>()
            .Where(n => n.UserId == userId && !n.IsDeleted)
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
            .CountAsync(n => n.UserId == userId && !n.IsRead && !n.IsDeleted);
        return Ok(count);
    }

    /// <summary>PATCH /api/notifications/{id}/read</summary>
    [HttpPatch("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var userId = GetUserId();
        var n = await _db.Set<Notification>()
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId && !n.IsDeleted);

        if (n == null) return NotFound();

        n.IsRead    = true;
        n.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>PATCH /api/notifications/read-all</summary>
    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var userId = GetUserId();
        var notifications = await _db.Set<Notification>()
            .Where(n => n.UserId == userId && !n.IsRead && !n.IsDeleted)
            .ToListAsync();

        foreach (var n in notifications)
        {
            n.IsRead    = true;
            n.UpdatedAt = DateTime.UtcNow;
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
        n.IsDeleted = true;
        n.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>DELETE /api/notifications/clear-all — مسح كل الإشعارات</summary>
    [HttpDelete("clear-all")]
    public async Task<IActionResult> ClearAll()
    {
        var userId = GetUserId();
        var notifications = await _db.Set<Notification>()
            .Where(n => n.UserId == userId && !n.IsDeleted)
            .ToListAsync();
        foreach (var n in notifications)
        {
            n.IsDeleted = true;
            n.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        return Ok(new { deleted = notifications.Count });
    }
}
