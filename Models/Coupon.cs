namespace Sportive.API.Models;

public enum DiscountType { Percentage, FixedAmount }

public class Coupon : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
    public DiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public int? MaxUsageCount { get; set; }
    public int CurrentUsageCount { get; set; } = 0;
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
}
