namespace Sportive.API.Models;

public enum NotificationType
{
    OrderPlaced   = 1,
    OrderConfirmed = 2,
    OrderShipped  = 3,
    OrderDelivered = 4,
    OrderCancelled = 5,
    General       = 6
}

public class Notification : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public string TitleAr { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;
    public string MessageAr { get; set; } = string.Empty;
    public string MessageEn { get; set; } = string.Empty;
    public NotificationType Type { get; set; } = NotificationType.General;
    public bool IsRead { get; set; } = false;
    public int? OrderId { get; set; }
}
