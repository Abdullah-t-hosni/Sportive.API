using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sportive.API.Models;

public class WishlistItem : BaseEntity
{
    public int ProductId { get; set; }
    [ForeignKey("ProductId")]
    public Product? Product { get; set; }

    public int CustomerId { get; set; }
    [ForeignKey("CustomerId")]
    public Customer? Customer { get; set; }
}

public class Notification : BaseEntity
{
    public int? CustomerId { get; set; }
    [ForeignKey("CustomerId")]
    public Customer? Customer { get; set; }

    public string TitleAr { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;
    public string MessageAr { get; set; } = string.Empty;
    public string MessageEn { get; set; } = string.Empty;
    
    public string? RelatedOrderNumber { get; set; }
    public bool IsRead { get; set; } = false;
    public string Type { get; set; } = "OrderUpdate"; // OrderUpdate, StockAlert, Promo
}
