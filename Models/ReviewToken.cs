namespace Sportive.API.Models;

/// <summary>
/// One-time review token sent to guest customers via WhatsApp.
/// Expires after 48 hours and can only be used once.
/// </summary>
public class ReviewToken : BaseEntity
{
    public string Token { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; } = false;
    public DateTime? UsedAt { get; set; }
}
