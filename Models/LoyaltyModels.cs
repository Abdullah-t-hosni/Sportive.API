using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Sportive.API.Utils;

namespace Sportive.API.Models;

public enum LoyaltyTransactionType
{
    Earned = 1,      // اكتساب نقاط من فاتورة
    Redeemed = 2,    // استبدال نقاط بخصم
    Reversed = 3,    // إلغاء نقاط مكتسبة بسبب مرتجع
    Adjusted = 4     // تعديل يدوي من الإدارة
}

public enum CustomerLoyaltyTier
{
    Bronze = 1,
    Silver = 2,
    Gold = 3,
    Platinum = 4
}

[Table("LoyaltyProgramSettings")]
public class LoyaltyProgramSettings
{
    [Key]
    public int Id { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    // معدل الاكتساب: كم نقطة لكل 1 وحدة نقدية (مثلاً 0.10 تعني 1 نقطة لكل 10 جنيهات)
    [Column(TypeName = "decimal(18,4)")]
    public decimal PointsPerCurrencyUnit { get; set; } = 0.1000m;

    // معدل الاستبدال: القيمة النقدية لكل 1 نقطة (مثلاً 0.10 تعني 1 نقطة = 0.10 ج، فكل 100 نقطة = 10 ج)
    [Column(TypeName = "decimal(18,4)")]
    public decimal CurrencyUnitPerPointRedeemed { get; set; } = 0.1000m;

    // الحد الأدنى من النقاط للاستبدال
    public int MinPointsToRedeem { get; set; } = 50;

    // أقصى نسبة خصم من إجمالي الفاتورة مسموح بها عبر النقاط
    [Column(TypeName = "decimal(18,2)")]
    public decimal MaxRedemptionPercentage { get; set; } = 50.00m;

    // صلاحية النقاط بالأيام (0 = لا تنتهي)
    public int PointsExpiryDays { get; set; } = 365;

    // حدود الشرائح ومضاعفاتها
    [Column(TypeName = "decimal(18,2)")]
    public decimal SilverThreshold { get; set; } = 3000.00m;
    [Column(TypeName = "decimal(18,2)")]
    public decimal SilverMultiplier { get; set; } = 1.25m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal GoldThreshold { get; set; } = 8000.00m;
    [Column(TypeName = "decimal(18,2)")]
    public decimal GoldMultiplier { get; set; } = 1.50m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal PlatinumThreshold { get; set; } = 20000.00m;
    [Column(TypeName = "decimal(18,2)")]
    public decimal PlatinumMultiplier { get; set; } = 2.00m;

    public DateTime UpdatedAt { get; set; } = TimeHelper.GetEgyptTime();
}

[Table("LoyaltyPointTransactions")]
public class LoyaltyPointTransaction : BaseEntity
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Points { get; set; } // موجبة للاكتساب وسالبة للاستبدال

    public LoyaltyTransactionType TransactionType { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal AmountEquivalent { get; set; } = 0;

    [MaxLength(500)]
    public string? Note { get; set; }

    [MaxLength(150)]
    public string? CreatedBy { get; set; }
}
