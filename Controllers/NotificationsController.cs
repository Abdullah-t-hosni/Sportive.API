using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;
    private readonly AppDbContext _db;

    public NotificationsController(INotificationService notifications, AppDbContext db)
    {
        _notifications = notifications;
        _db = db;
    }

    private async Task<int?> GetCustomerIdAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return null;
        var customer = await _db.Customers
            .Where(c => c.AppUserId == userId && !c.IsDeleted)
            .FirstOrDefaultAsync();
        return customer?.Id;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var custId = await GetCustomerIdAsync();
        if (custId == null) return NotFound(new { message = "Customer not found" });
        return Ok(await _notifications.GetMyNotificationsAsync(custId.Value));
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var custId = await GetCustomerIdAsync();
        if (custId == null) return Ok(0);
        return Ok(await _notifications.GetUnreadCountAsync(custId.Value));
    }

    [HttpPatch("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        await _notifications.MarkAsReadAsync(id);
        return Ok();
    }
}
