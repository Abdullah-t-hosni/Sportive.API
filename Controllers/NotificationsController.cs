using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;
    public NotificationsController(INotificationService notificationService) => _notificationService = notificationService;

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>GET /api/notifications</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int count = 50)
    {
        var notifications = await _notificationService.GetMyNotificationsAsync(GetUserId(), count);
        return Ok(notifications);
    }

    /// <summary>GET /api/notifications/unread-count</summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var count = await _notificationService.GetUnreadCountAsync(GetUserId());
        return Ok(count);
    }

    /// <summary>PATCH /api/notifications/{id}/read</summary>
    [HttpPatch("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        await _notificationService.MarkAsReadAsync(GetUserId(), id);
        return Ok();
    }

    /// <summary>PATCH /api/notifications/read-all</summary>
    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        await _notificationService.MarkAllAsReadAsync(GetUserId());
        return Ok();
    }

    /// <summary>DELETE /api/notifications/{id}</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _notificationService.DeleteAsync(GetUserId(), id);
        return Ok();
    }

    /// <summary>DELETE /api/notifications/clear-all</summary>
    [HttpDelete("clear-all")]
    public async Task<IActionResult> ClearAll()
    {
        await _notificationService.ClearAllAsync(GetUserId());
        return Ok();
    }
}
