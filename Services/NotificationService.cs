using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Services;

public interface INotificationService
{
    Task SendAsync(string? userId, string titleAr, string titleEn, string msgAr, string msgEn, string type = "General", int? orderId = null);
    Task<List<Notification>> GetMyNotificationsAsync(string userId);
    Task MarkAsReadAsync(int notificationId);
    Task<int> GetUnreadCountAsync(string userId);
}

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    public NotificationService(AppDbContext db) => _db = db;

    public async Task SendAsync(
        string? userId, string titleAr, string titleEn, string msgAr, string msgEn, 
        string type = "General", int? orderId = null)
    {
        _db.Notifications.Add(new Notification {
            UserId = userId ?? string.Empty,
            TitleAr = titleAr,
            TitleEn = titleEn,
            MessageAr = msgAr,
            MessageEn = msgEn,
            Type = type,
            OrderId = orderId
        });
        await _db.SaveChangesAsync();
    }

    public async Task<List<Notification>> GetMyNotificationsAsync(string userId)
    {
        return await _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync();
    }

    public async Task MarkAsReadAsync(int notificationId)
    {
        var n = await _db.Notifications.FindAsync(notificationId);
        if (n != null) { n.IsRead = true; await _db.SaveChangesAsync(); }
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        return await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
    }
}
