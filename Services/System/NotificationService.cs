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
        var prefix = GetPrefix();
        var notificationsToSave = new List<Notification>();
        var adminUserIds = new List<string>();

        // 1. If it's an order or a general alert, we must notify all Admins/Staff
        if (type == "Order" || type == "Alert" || string.IsNullOrEmpty(userId))
        {
            var adminRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
            if (adminRole != null)
            {
                adminUserIds = await _db.UserRoles
                    .Where(ur => ur.RoleId == adminRole.Id)
                    .Select(ur => ur.UserId)
                    .ToListAsync();
            }
        }

        // Add the specific user notification if applicable
        if (!string.IsNullOrEmpty(finalUserId))
        {
            notificationsToSave.Add(new Notification {
                UserId = finalUserId,
                TitleAr = titleAr,
                TitleEn = titleEn,
                MessageAr = msgAr,
                MessageEn = msgEn,
                Type = type,
                OrderId = orderId
            });
        }

        // Add notifications for all admins
        foreach (var adminId in adminUserIds)
        {
            if (adminId != finalUserId)
            {
                notificationsToSave.Add(new Notification {
                    UserId = adminId,
                    TitleAr = titleAr,
                    TitleEn = titleEn,
                    MessageAr = msgAr,
                    MessageEn = msgEn,
                    Type = type,
                    OrderId = orderId
                });
            }
        }

        if (notificationsToSave.Any())
        {
            _db.Notifications.AddRange(notificationsToSave);
            await _db.SaveChangesAsync();
        }

        // The first notification is used as a template for broadcasting to groups where specific ID doesn't matter as much
        var primaryNotif = notificationsToSave.FirstOrDefault();
        if (primaryNotif == null) return;

        var payload = new {
            primaryNotif.Id,
            primaryNotif.TitleAr,
            primaryNotif.TitleEn,
            primaryNotif.MessageAr,
            primaryNotif.MessageEn,
            primaryNotif.Type,
            primaryNotif.IsRead,
            primaryNotif.OrderId,
            primaryNotif.CreatedAt
        };

        // 1. Send to the specific user if exists
        if (!string.IsNullOrEmpty(finalUserId))
        {
            var userNotif = notificationsToSave.FirstOrDefault(n => n.UserId == finalUserId) ?? primaryNotif;
            var userPayload = new {
                userNotif.Id, userNotif.TitleAr, userNotif.TitleEn, userNotif.MessageAr, 
                userNotif.MessageEn, userNotif.Type, userNotif.IsRead, userNotif.OrderId, userNotif.CreatedAt
            };
            
            await _hubContext.Clients.Group($"{prefix}_{finalUserId}").SendAsync("ReceiveNotification", userPayload);
            var unreadCount = await GetUnreadCountAsync(finalUserId);
            await _hubContext.Clients.Group($"{prefix}_{finalUserId}").SendAsync("ReceiveUnreadCount", unreadCount);
            _ = Task.Run(() => SendWebPushAsync(finalUserId, titleAr, titleEn, msgAr, msgEn, type, orderId));
        }

        // 2. Send to Admins
        if (type == "Order" || type == "Alert" || string.IsNullOrEmpty(userId))
        {
            await _hubContext.Clients.Group($"{prefix}_Admin").SendAsync("ReceiveNotification", payload);
            
            foreach (var adminId in adminUserIds)
            {
                if (adminId != finalUserId)
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
