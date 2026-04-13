namespace Sportive.API.Models;

/// <summary>
/// جدول مدفوعات الطلب — بديل احترافي عن تخزين بيانات الدفع في AdminNotes بتنسيق JSON
/// كل طلب يمكن أن يحتوي على أكثر من وسيلة دفع (Mixed Payment)
/// </summary>
public class OrderPayment : BaseEntity
{
    public int           OrderId       { get; set; }
    public Order         Order         { get; set; } = null!;

    /// <summary>وسيلة الدفع لهذا الجزء</summary>
    public PaymentMethod Method        { get; set; }

    /// <summary>المبلغ المدفوع بهذه الوسيلة</summary>
    public decimal       Amount        { get; set; }

    /// <summary>رقم مرجعي للعملية (مثل: رقم تحويل فودافون، إيصال إنستاباي)</summary>
    public string?       Reference     { get; set; }

    /// <summary>ملاحظات إضافية على هذه الدفعة</summary>
    public string?       Notes         { get; set; }

    /// <summary>هل تمت معالجة هذه الدفعة محاسبياً؟</summary>
    public bool          IsPosted      { get; set; } = false;
}
