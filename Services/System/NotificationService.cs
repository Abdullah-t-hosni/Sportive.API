using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Hubs;
using Sportive.API.Models;
using Sportive.API.Utils;

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
    Task BroadcastStockUpdateAsync(int productId, int variantId, int newStock);
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
        var payload = new {
            notification.Id,
            notification.TitleAr,
            notification.TitleEn,
            notification.MessageAr,
            notification.MessageEn,
            notification.Type,
            notification.IsRead,
            notification.OrderId,
            notification.CreatedAt
        };

        // 1. Send to the specific user if exists
        if (!string.IsNullOrEmpty(finalUserId))
        {
            await _hubContext.Clients.Group(finalUserId).SendAsync("ReceiveNotification", payload);
            
            var unreadCount = await GetUnreadCountAsync(finalUserId);
            await _hubContext.Clients.Group(finalUserId).SendAsync("ReceiveUnreadCount", unreadCount);
        }

        // 2. IMPORTANT: If it's an order or a general alert, inform all Admins/Staff
        if (type == "Order" || type == "Alert" || string.IsNullOrEmpty(userId))
        {
            await _hubContext.Clients.Group("Admin").SendAsync("ReceiveNotification", payload);
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
            n.UpdatedAt = TimeHelper.GetEgyptTime();
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
                n.UpdatedAt = TimeHelper.GetEgyptTime();
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
            var wasUnread = !n.IsRead;
            _db.Notifications.Remove(n);
            await _db.SaveChangesAsync();

            if (wasUnread)
            {
                var unreadCount = await GetUnreadCountAsync(userId);
                await _hubContext.Clients.Group(userId).SendAsync("ReceiveUnreadCount", unreadCount);
            }
        }
    }

    public async Task ClearAllAsync(string userId)
    {
        var all = await _db.Notifications.Where(n => n.UserId == userId).ToListAsync();
        _db.Notifications.RemoveRange(all);
        await _db.SaveChangesAsync();
        await _hubContext.Clients.Group(userId).SendAsync("ReceiveUnreadCount", 0);
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        return await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
    }

    public async Task BroadcastStockUpdateAsync(int productId, int variantId, int newStock)
    {
        await _hubContext.Clients.Group("Admin")
            .SendAsync("StockUpdate", new { productId, variantId, newStock });
    }
}
