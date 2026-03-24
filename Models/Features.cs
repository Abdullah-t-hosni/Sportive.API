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
    public string UserId { get; set; } = string.Empty;  // FK → AspNetUsers.Id

    public string TitleAr  { get; set; } = string.Empty;
    public string TitleEn  { get; set; } = string.Empty;
    public string MessageAr { get; set; } = string.Empty;
    public string MessageEn { get; set; } = string.Empty;
    public string Type     { get; set; } = "General"; // OrderUpdate | StockAlert | Promo | General
    public bool   IsRead   { get; set; } = false;
    public int?   OrderId  { get; set; }
}
