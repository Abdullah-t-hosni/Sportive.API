// ============================================================
// Models/AuditLog.cs
// ✅ جديد — سجل تدقيق لتتبع التغييرات الحساسة
// ============================================================
using System.ComponentModel.DataAnnotations;
using Sportive.API.Utils;

namespace Sportive.API.Models;

/// <summary>
/// يسجل كل عملية حساسة: تغيير أسعار، تعديل طلبات، حذف بيانات...
/// </summary>
public class AuditLog
{
    [Key]
    public long Id { get; set; }

    /// <summary>معرف المستخدم الذي قام بالعملية</summary>
    [MaxLength(450)]
    public string? UserId { get; set; }

    /// <summary>اسم المستخدم (snapshot وقت الحدث)</summary>
    [MaxLength(200)]
    public string? UserName { get; set; }

    /// <summary>نوع العملية: Create, Update, Delete, Login, Export...</summary>
    [Required]
    [MaxLength(50)]
    public string Action { get; set; } = string.Empty;

    /// <summary>الجدول أو الكيان المتأثر: Order, Product, Customer...</summary>
    [Required]
    [MaxLength(100)]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>معرف السجل المتأثر</summary>
    [MaxLength(100)]
    public string? EntityId { get; set; }

    /// <summary>القيمة القديمة (JSON)</summary>
    public string? OldValues { get; set; }

    /// <summary>القيمة الجديدة (JSON)</summary>
    public string? NewValues { get; set; }

    /// <summary>تفاصيل إضافية أو سبب التغيير</summary>
    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>عنوان IP مصدر الطلب</summary>
    [MaxLength(50)]
    public string? IpAddress { get; set; }

    /// <summary>وقت الحدث — دائماً UTC</summary>
    public DateTime CreatedAt { get; set; } = TimeHelper.GetEgyptTime();
}
