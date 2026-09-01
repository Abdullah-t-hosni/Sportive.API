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

    private static bool MatchesPreference(string type, string preference)
    {
        if (string.Equals(type, preference, StringComparison.OrdinalIgnoreCase)) return true;

        if (IsOnlineOrder(type) && IsOnlineOrder(preference)) return true;
        if (IsPosOrder(type) && IsPosOrder(preference)) return true;
        if (IsWhatsAppType(type) && IsWhatsAppType(preference)) return true;
        if (IsStockType(type) && IsStockType(preference)) return true;
        if (IsAlertType(type) && IsAlertType(preference)) return true;
        if (IsSystemType(type) && IsSystemType(preference)) return true;

        return false;
    }

    private static bool IsOnlineOrder(string t) =>
        t.Equals("OnlineOrder", StringComparison.OrdinalIgnoreCase) ||
        t.Equals("Order", StringComparison.OrdinalIgnoreCase) ||
        t.Equals("Orders", StringComparison.OrdinalIgnoreCase);

    private static bool IsPosOrder(string t) =>
        t.Equals("POSOrder", StringComparison.OrdinalIgnoreCase);

    private static bool IsWhatsAppType(string t) =>
        t.Equals("WhatsApp", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("الواتساب", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("واتساب", StringComparison.OrdinalIgnoreCase);

    private static bool IsStockType(string t) =>
        t.Equals("Stock", StringComparison.OrdinalIgnoreCase) ||
        t.Equals("StockAlert", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("المخزون", StringComparison.OrdinalIgnoreCase);

    private static bool IsAlertType(string t) =>
        t.Equals("Alert", StringComparison.OrdinalIgnoreCase) ||
        t.Equals("General", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("تنبيه", StringComparison.OrdinalIgnoreCase);

    private static bool IsSystemType(string t) =>
        t.Equals("System", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("نظام", StringComparison.OrdinalIgnoreCase);

    public async Task SendAsync(
        string? userId, string titleAr, string titleEn, string msgAr, string msgEn, 
        string type = "General", int? orderId = null)
    {
        var finalUserId = userId ?? string.Empty;
        var prefix = GetPrefix();
        var notificationsToSave = new List<Notification>();
        var adminUserIds = new List<string>();

        // 1. Determine if this notification should go to staff/admins
        if (type == "Order" || type == "OnlineOrder" || type == "POSOrder" || type == "WhatsApp" || type == "Alert" || type == "Stock" || type == "System" || string.IsNullOrEmpty(userId))
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
                var roles = rolesByUserId.ContainsKey(u.Id) ? rolesByUserId[u.Id] : new List<string>();

                bool isStaffOrAdmin = roles.Any(r => 
                    r.Equals("Admin", StringComparison.OrdinalIgnoreCase) || 
                    r.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) || 
                    r.Equals("Super Admin", StringComparison.OrdinalIgnoreCase) ||
                    r.Equals("Manager", StringComparison.OrdinalIgnoreCase) ||
                    r.Equals("Staff", StringComparison.OrdinalIgnoreCase) ||
                    r.Equals("Cashier", StringComparison.OrdinalIgnoreCase) ||
                    r.Equals("Moderator", StringComparison.OrdinalIgnoreCase)
                );

                if (!isStaffOrAdmin) continue;

                bool shouldNotify = false;

                // If user has specific NotificationPreferences array set
                if (!string.IsNullOrWhiteSpace(u.NotificationPreferences) && u.NotificationPreferences.Trim() != "[]")
                {
                    try
                    {
                        var prefs = JsonSerializer.Deserialize<List<string>>(u.NotificationPreferences);
                        if (prefs != null && prefs.Any())
                        {
                            shouldNotify = prefs.Any(p => MatchesPreference(type, p));
                        }
                        else
                        {
                            shouldNotify = true;
                        }
                    }
                    catch
                    {
                        shouldNotify = true;
                    }
                }
                else
                {
                    // By default, ALL staff and admins receive ALL store notifications (including WhatsApp, Orders, Stock, System, Alerts)
                    shouldNotify = true;
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

        // Add notifications for all admins/staff
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
                notif.Id,
                id = notif.Id,
                notif.TitleAr,
                titleAr = notif.TitleAr,
                notif.TitleEn,
                titleEn = notif.TitleEn,
                notif.MessageAr,
                messageAr = notif.MessageAr,
                notif.MessageEn,
                messageEn = notif.MessageEn,
                notif.Type,
                type = notif.Type,
                notif.IsRead,
                isRead = notif.IsRead,
                notif.OrderId,
                orderId = notif.OrderId,
                notif.CreatedAt,
                createdAt = notif.CreatedAt
            };
            
            // Broadcast to tenant group, global group, and raw user group for 100% SignalR delivery
            await _hubContext.Clients.Group($"{prefix}_{notif.UserId}").SendAsync("ReceiveNotification", userPayload);
            await _hubContext.Clients.Group($"global_{notif.UserId}").SendAsync("ReceiveNotification", userPayload);
            await _hubContext.Clients.Group($"user_{notif.UserId}").SendAsync("ReceiveNotification", userPayload);

            var unreadCount = await GetUnreadCountAsync(notif.UserId);
            await _hubContext.Clients.Group($"{prefix}_{notif.UserId}").SendAsync("ReceiveUnreadCount", unreadCount);
            await _hubContext.Clients.Group($"global_{notif.UserId}").SendAsync("ReceiveUnreadCount", unreadCount);
            await _hubContext.Clients.Group($"user_{notif.UserId}").SendAsync("ReceiveUnreadCount", unreadCount);
            
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

            var subject = Environment.GetEnvironmentVariable("VAPID_SUBJECT") ?? _config["Vapid:Subject"];
            var publicKey = Environment.GetEnvironmentVariable("VAPID_PUBLIC_KEY") ?? _config["Vapid:PublicKey"];
            var privateKey = Environment.GetEnvironmentVariable("VAPID_PRIVATE_KEY") ?? _config["Vapid:PrivateKey"];

            // If it's a literal placeholder from appsettings.json, treat it as empty so it fails cleanly or falls back
            if (subject == "${VAPID_SUBJECT}") subject = null;
            if (publicKey == "${VAPID_PUBLIC_KEY}") publicKey = null;
            if (privateKey == "${VAPID_PRIVATE_KEY}") privateKey = null;

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
