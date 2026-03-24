using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Services;

public interface INotificationService
{
    Task SendAsync(int? customerId, string titleAr, string titleEn, string msgAr, string msgEn, string type = "OrderUpdate", string? relatedOrder = null);
    Task<List<Notification>> GetMyNotificationsAsync(int customerId);
    Task MarkAsReadAsync(int notificationId);
    Task<int> GetUnreadCountAsync(int customerId);
}

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    public NotificationService(AppDbContext db) => _db = db;

    public async Task SendAsync(
        int? customerId, string titleAr, string titleEn, string msgAr, string msgEn, 
        string type = "OrderUpdate", string? relatedOrder = null)
    {
        _db.Notifications.Add(new Notification {
            CustomerId = customerId,
            TitleAr = titleAr,
            TitleEn = titleEn,
            MessageAr = msgAr,
            MessageEn = msgEn,
            Type = type,
            RelatedOrderNumber = relatedOrder
        });
        await _db.SaveChangesAsync();
    }

    public async Task<List<Notification>> GetMyNotificationsAsync(int customerId)
    {
        return await _db.Notifications
            .Where(n => n.CustomerId == customerId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync();
    }

    public async Task MarkAsReadAsync(int notificationId)
    {
        var n = await _db.Notifications.FindAsync(notificationId);
        if (n != null) { n.IsRead = true; await _db.SaveChangesAsync(); }
    }

    public async Task<int> GetUnreadCountAsync(int customerId)
    {
        return await _db.Notifications.CountAsync(n => n.CustomerId == customerId && !n.IsRead);
    }
}
