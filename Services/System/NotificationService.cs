using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Hubs;
using Sportive.API.Models;
using Sportive.API.Utils;
using Microsoft.Extensions.Configuration;
using WebPush;
using System.Text.Json;
using Microsoft.Extensions.Logging;

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
    private readonly Sportive.API.Interfaces.ITenantContext _tenantContext;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        AppDbContext db, 
        IHubContext<NotificationHub> hubContext, 
        Sportive.API.Interfaces.ITenantContext tenantContext,
        IConfiguration config,
        ILogger<NotificationService> logger)
    {
        _db = db;
        _hubContext = hubContext;
        _tenantContext = tenantContext;
        _config = config;
        _logger = logger;
    }

    private string GetPrefix() => _tenantContext.CurrentTenant?.Slug?.ToLowerInvariant() ?? "global";

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

        var prefix = GetPrefix();

        // 1. Send to the specific user if exists
        if (!string.IsNullOrEmpty(finalUserId))
        {
            await _hubContext.Clients.Group($"{prefix}_{finalUserId}").SendAsync("ReceiveNotification", payload);
            
            var unreadCount = await GetUnreadCountAsync(finalUserId);
            await _hubContext.Clients.Group($"{prefix}_{finalUserId}").SendAsync("ReceiveUnreadCount", unreadCount);

            // Send Web Push
            _ = Task.Run(() => SendWebPushAsync(finalUserId, titleAr, titleEn, msgAr, msgEn, type, orderId));
        }

        // 2. IMPORTANT: If it's an order or a general alert, inform all Admins/Staff
        if (type == "Order" || type == "Alert" || string.IsNullOrEmpty(userId))
        {
            await _hubContext.Clients.Group($"{prefix}_Admin").SendAsync("ReceiveNotification", payload);
            
            // Get all admin users
            var adminRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
            if (adminRole != null)
            {
                var adminUserIds = await _db.UserRoles
                    .Where(ur => ur.RoleId == adminRole.Id)
                    .Select(ur => ur.UserId)
                    .ToListAsync();
                    
                foreach (var adminId in adminUserIds)
                {
                    _ = Task.Run(() => SendWebPushAsync(adminId, titleAr, titleEn, msgAr, msgEn, type, orderId));
                }
            }
        }
    }

    private async Task SendWebPushAsync(string userId, string titleAr, string titleEn, string msgAr, string msgEn, string type, int? orderId)
    {
        try
        {
            using var scope = _db.Database.GetDbConnection().CreateCommand();
            // Need a fresh DbContext for background task since _db might be disposed
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseMySql(_config.GetConnectionString("DefaultConnection"), ServerVersion.AutoDetect(_config.GetConnectionString("DefaultConnection")));
            using var db = new AppDbContext(optionsBuilder.Options);

            var subscriptions = await db.PushSubscriptions
                .Where(s => s.UserId == userId)
                .ToListAsync();

            if (!subscriptions.Any()) return;

            var subject = _config["Vapid:Subject"];
            var publicKey = _config["Vapid:PublicKey"];
            var privateKey = _config["Vapid:PrivateKey"];

            if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(privateKey))
                return;

            var vapidDetails = new VapidDetails(subject, publicKey, privateKey);
            var webPushClient = new WebPushClient();

            var payload = JsonSerializer.Serialize(new
            {
                titleAr,
                titleEn,
                msgAr,
                msgEn,
                type,
                orderId
            });

            foreach (var sub in subscriptions)
            {
                try
                {
                    var pushSubscription = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                    await webPushClient.SendNotificationAsync(pushSubscription, payload, vapidDetails);
                    
                    // Update LastUsedAt
                    sub.LastUsedAt = DateTime.UtcNow;
                }
                catch (WebPushException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Gone || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Subscription has expired or is no longer valid
                    db.PushSubscriptions.Remove(sub);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send web push notification to endpoint {Endpoint}", sub.Endpoint);
                }
            }
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing web push notifications for user {UserId}", userId);
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
            var prefix = GetPrefix();
            await _hubContext.Clients.Group($"{prefix}_{userId}").SendAsync("ReceiveUnreadCount", unreadCount);
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
            var prefix = GetPrefix();
            await _hubContext.Clients.Group($"{prefix}_{userId}").SendAsync("ReceiveUnreadCount", 0);
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
                var prefix = GetPrefix();
                await _hubContext.Clients.Group($"{prefix}_{userId}").SendAsync("ReceiveUnreadCount", unreadCount);
            }
        }
    }

    public async Task ClearAllAsync(string userId)
    {
        var all = await _db.Notifications.Where(n => n.UserId == userId).ToListAsync();
        _db.Notifications.RemoveRange(all);
        await _db.SaveChangesAsync();
        var prefix = GetPrefix();
        await _hubContext.Clients.Group($"{prefix}_{userId}").SendAsync("ReceiveUnreadCount", 0);
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        return await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
    }

    public async Task BroadcastStockUpdateAsync(int productId, int variantId, int newStock)
    {
        var prefix = GetPrefix();
        await _hubContext.Clients.Group($"{prefix}_Admin")
            .SendAsync("StockUpdate", new { productId, variantId, newStock });
    }
}
