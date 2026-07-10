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
    private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopeFactory;

    public NotificationService(
        AppDbContext db, 
        IHubContext<NotificationHub> hubContext, 
        Sportive.API.Interfaces.ITenantContext tenantContext,
        IConfiguration config,
        ILogger<NotificationService> logger,
        Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _hubContext = hubContext;
        _tenantContext = tenantContext;
        _config = config;
        _logger = logger;
        _scopeFactory = scopeFactory;
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

        // 1. Determine if this notification should go to staff/admins
        if (type == "Order" || type == "OnlineOrder" || type == "POSOrder" || type == "Alert" || type == "Stock" || type == "System" || string.IsNullOrEmpty(userId))
        {
            var users = await _db.Users.ToListAsync();

            var userRolesData = await (from ur in _db.UserRoles
                                       join r in _db.Roles on ur.RoleId equals r.Id
                                       select new { ur.UserId, RoleName = r.Name })
                                       .ToListAsync();

            var rolesByUserId = userRolesData
                                .GroupBy(x => x.UserId)
                                .ToDictionary(g => g.Key, g => g.Select(x => x.RoleName).ToList());

            foreach (var u in users)
            {
                bool shouldNotify = false;
                var roles = rolesByUserId.ContainsKey(u.Id) ? rolesByUserId[u.Id] : new List<string?>();

                if (!string.IsNullOrEmpty(u.NotificationPreferences))
                {
                    try
                    {
                        var prefs = JsonSerializer.Deserialize<List<string>>(u.NotificationPreferences);
                        if (prefs != null && prefs.Contains(type))
                        {
                            shouldNotify = true;
                        }
                    }
                    catch { } // Ignore malformed JSON
                }
                else
                {
                    // Fallback: If no preferences are set, enable for Admin and SuperAdmin
                    if (roles.Contains("Admin") || roles.Contains("SuperAdmin") || roles.Contains("Super Admin"))
                    {
                        shouldNotify = true;
                    }
                }

                if (shouldNotify)
                {
                    adminUserIds.Add(u.Id);
                }
            }

            adminUserIds = adminUserIds.Distinct().ToList();
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

        // Broadcast each saved notification to its respective owner in real-time
        foreach (var notif in notificationsToSave)
        {
            var userPayload = new {
                notif.Id, notif.TitleAr, notif.TitleEn, notif.MessageAr, 
                notif.MessageEn, notif.Type, notif.IsRead, notif.OrderId, notif.CreatedAt
            };
            
            await _hubContext.Clients.Group($"{prefix}_{notif.UserId}").SendAsync("ReceiveNotification", userPayload);
            var unreadCount = await GetUnreadCountAsync(notif.UserId);
            await _hubContext.Clients.Group($"{prefix}_{notif.UserId}").SendAsync("ReceiveUnreadCount", unreadCount);
            
            _ = Task.Run(() => SendWebPushAsync(notif.UserId, titleAr, titleEn, msgAr, msgEn, type, orderId));
        }
    }

    private async Task SendWebPushAsync(string userId, string titleAr, string titleEn, string msgAr, string msgEn, string type, int? orderId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
