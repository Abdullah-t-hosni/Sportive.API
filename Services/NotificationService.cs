using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Hubs;
using Sportive.API.Models;

namespace Sportive.API.Services;

public interface INotificationService
{
    Task SendAsync(string? userId, string titleAr, string titleEn, string msgAr, string msgEn, string type = "General", int? orderId = null);
    Task<List<Notification>> GetMyNotificationsAsync(string userId, int count = 50);
    Task MarkAsReadAsync(string userId, int notificationId);
    Task MarkAllAsReadAsync(string userId);
    Task DeleteAsync(string userId, int notificationId);
    Task ClearAllAsync(string userId);
    Task<int> GetUnreadCountAsync(string userId);
}

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotificationHub> _hubContext;

    public NotificationService(AppDbContext db, IHubContext<NotificationHub> hubContext)
    {
        _db = db;
        _hubContext = hubContext;
    }

    public async Task SendAsync(
        string? userId, string titleAr, string titleEn, string msgAr, string msgEn, 
        string type = "General", int? orderId = null)
    {
        var finalUserId = userId ?? string.Empty;
        var notification = new Notification {
            UserId = finalUserId,
            TitleAr = titleAr,
            TitleEn = titleEn,
            MessageAr = msgAr,
            MessageEn = msgEn,
            Type = type,
            OrderId = orderId
        };

        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync();

        // Push to SignalR
        if (!string.IsNullOrEmpty(finalUserId))
        {
            await _hubContext.Clients.Group(finalUserId).SendAsync("ReceiveNotification", new {
                notification.Id,
                notification.TitleAr,
                notification.TitleEn,
                notification.MessageAr,
                notification.MessageEn,
                notification.Type,
                notification.IsRead,
                notification.OrderId,
                notification.CreatedAt
            });
            
            var unreadCount = await GetUnreadCountAsync(finalUserId);
            await _hubContext.Clients.Group(finalUserId).SendAsync("ReceiveUnreadCount", unreadCount);
        }
    }

    public async Task<List<Notification>> GetMyNotificationsAsync(string userId, int count = 50)
    {
        return await _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task MarkAsReadAsync(string userId, int notificationId)
    {
        var n = await _db.Notifications
            .FirstOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId);
        
        if (n != null && !n.IsRead) 
        { 
            n.IsRead = true; 
            n.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(); 

            var unreadCount = await GetUnreadCountAsync(userId);
            await _hubContext.Clients.Group(userId).SendAsync("ReceiveUnreadCount", unreadCount);
        }
    }

    public async Task MarkAllAsReadAsync(string userId)
    {
        var unread = await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        if (unread.Any())
        {
            foreach (var n in unread)
            {
                n.IsRead = true;
                n.UpdatedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
            await _hubContext.Clients.Group(userId).SendAsync("ReceiveUnreadCount", 0);
        }
    }

    public async Task DeleteAsync(string userId, int notificationId)
    {
        var n = await _db.Notifications
            .FirstOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId);
            
        if (n != null)
        {
            n.IsDeleted = true;
            n.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            if (!n.IsRead)
            {
                var unreadCount = await GetUnreadCountAsync(userId);
                await _hubContext.Clients.Group(userId).SendAsync("ReceiveUnreadCount", unreadCount);
            }
        }
    }

    public async Task ClearAllAsync(string userId)
    {
        var all = await _db.Notifications.Where(n => n.UserId == userId).ToListAsync();
        foreach (var n in all)
        {
            n.IsDeleted = true;
            n.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        await _hubContext.Clients.Group(userId).SendAsync("ReceiveUnreadCount", 0);
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        return await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
    }
}
