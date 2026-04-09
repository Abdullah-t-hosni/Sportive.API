namespace Sportive.API.Models;

/// <summary>
/// يمثل قسطاً واحداً أو مديونية واجبة السداد على عميل معين.
/// يمكن إنشاؤه يدوياً أو ربطه بأمر شراء آجل.
/// </summary>
public class CustomerInstallment : BaseEntity
{
    public int    CustomerId    { get; set; }
    public Customer Customer   { get; set; } = null!;

    public int?   OrderId      { get; set; }
    public Order? Order        { get; set; }

    /// <summary>المبلغ الإجمالي للقسط</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>المبلغ المسدَّد حتى الآن</summary>
    public decimal PaidAmount  { get; set; } = 0;

    public decimal RemainingAmount => TotalAmount - PaidAmount;

    /// <summary>تاريخ الاستحقاق</summary>
    public DateTime DueDate    { get; set; }

    /// <summary>ملاحظة أو وصف</summary>
    public string? Note        { get; set; }

    public InstallmentStatus Status { get; set; } = InstallmentStatus.Pending;

    public ICollection<InstallmentPayment> Payments { get; set; } = new List<InstallmentPayment>();
}

public enum InstallmentStatus
{
    Pending   = 1, // في الانتظار
    Partial   = 2, // مدفوع جزئياً
    Paid      = 3, // مدفوع بالكامل
    Overdue   = 4, // متأخر
    Cancelled = 5  // ملغي
}

/// <summary>سجل دفعة على قسط معين</summary>
public class InstallmentPayment : BaseEntity
{
    public int CustomerInstallmentId { get; set; }
    public CustomerInstallment Installment { get; set; } = null!;

    public decimal Amount      { get; set; }
    public DateTime PaymentDate { get; set; }
    public string? Note        { get; set; }
    public string? CollectedBy { get; set; } // اسم أو ID الموظف
}
