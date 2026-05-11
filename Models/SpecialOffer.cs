namespace Sportive.API.Models;

/// <summary>
/// نظام عروض الكميات الجديد
/// يسمح بتحديد عدد قطع (Threshold) وتطبيق خصم على القطع التي تزيد عن هذا العدد
/// يطبق الخصم دائماً على القطع الأقل سعراً لضمان العدالة
/// </summary>
public class SpecialOffer : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>عدد القطع المطلوب شراؤها بالسعر الكامل قبل تطبيق الخصم</summary>
    public int ThresholdQuantity { get; set; }

    /// <summary>عدد القطع الهدية في نظام المجموعات (مثلاً: 7 في عرض 3+7)</summary>
    public int? FreeQuantity { get; set; }

    /// <summary>قيمة الخصم (نسبة مئوية) على القطع الإضافية</summary>
    public decimal DiscountPercentage { get; set; }

    /// <summary>هل الخصم كامل (100%)؟</summary>
    public bool IsFullDiscount { get; set; }

    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>تحديد مكان تطبيق العرض (المتجر، الكاشير، أو الكل)</summary>
    public DiscountApplyTo ApplyTo { get; set; } = DiscountApplyTo.All;

    /// <summary>قائمة المعرفات للأقسام المشمولة (مفصولة بفاصلة)</summary>
    public string? EligibleCategoryIds { get; set; }

    /// <summary>قائمة المعرفات للماركات المشمولة (مفصولة بفاصلة)</summary>
    public string? EligibleBrandIds { get; set; }
}
