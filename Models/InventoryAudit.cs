using System;
using System.Collections.Generic;

namespace Sportive.API.Models;

public enum InventoryAuditStatus
{
    Draft     = 1,   // مسودة (جاري الجرد)
    Reviewing = 2,   // قيد المراجعة
    Posted    = 3,   // معتمد (تم تعديل المخزون)
    Cancelled = 4    // ملغي
}

/// <summary>
/// يمثل جلسة جرد مخزني (Inventory Audit Session)
/// </summary>
public class InventoryAudit : BaseEntity
{
    public string Title        { get; set; } = string.Empty;  // عنوان الجرد (مثلاً جرد مخزن رئيسي 2026)
    public DateTime AuditDate  { get; set; } = DateTime.UtcNow;
    public string? Description { get; set; }
    public string? CreatedByUserId { get; set; }
    public InventoryAuditStatus Status { get; set; } = InventoryAuditStatus.Draft;

    // المجموع المالي لفوارق الجرد (سواء عجز أو زيادة)
    public decimal TotalExpectedValue { get; set; } = 0;
    public decimal TotalActualValue   { get; set; } = 0;
    public decimal ValueDifference    => TotalActualValue - TotalExpectedValue;

    public ICollection<InventoryAuditItem> Items { get; set; } = new List<InventoryAuditItem>();
    
    // ربط بالقيد المحاسبي في حال تم اعتماده
    public int? JournalEntryId { get; set; }
    public JournalEntry? JournalEntry { get; set; }
}

/// <summary>
/// يمثل تفاصيل صنف في جلسة الجرد
/// </summary>
public class InventoryAuditItem : BaseEntity
{
    public int InventoryAuditId { get; set; }
    public InventoryAudit InventoryAudit { get; set; } = null!;

    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    
    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public int ExpectedQuantity { get; set; } = 0;  // الكمية في السيستم
    public int ActualQuantity   { get; set; } = 0;  // الكمية الفعلية بعد العد
    public int Difference       => ActualQuantity - ExpectedQuantity;

    public decimal UnitCost     { get; set; } = 0;  // سعر التكلفة وقت الجرد
    public string? Note         { get; set; }
}
