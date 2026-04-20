namespace Sportive.API.Models;

/// <summary>
/// خصم مؤقت على منتج — يُفعَّل تلقائياً بين ValidFrom و ValidTo
/// يمكن تقييده بحد أدنى للكمية (MinQty)
/// </summary>
public class ProductDiscount : BaseEntity
{
    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    /// <summary>نوع الخصم: مبلغ ثابت أو نسبة مئوية</summary>
    public DiscountType DiscountType { get; set; } = DiscountType.Percentage;

    /// <summary>قيمة الخصم (نسبة أو مبلغ)</summary>
    public decimal DiscountValue { get; set; }

    /// <summary>الحد الأدنى للكمية لتفعيل الخصم (0 = بدون حد)</summary>
    public int MinQty { get; set; } = 0;

    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo   { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Label { get; set; } // e.g. "عرض العيد", "Black Friday"
    public DiscountApplyTo ApplyTo { get; set; } = DiscountApplyTo.All;
}

public enum DiscountApplyTo
{
    All = 0,
    Store = 1, // Website/Online
    POS = 2    // In-store
}
