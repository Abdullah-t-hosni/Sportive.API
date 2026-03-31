using System;

namespace Sportive.API.Models;

public enum InventoryMovementType
{
    OpeningBalance = 1, // رصيد افتتاحِي
    Purchase       = 2, // مشتريات (+)
    Sale           = 3, // مبيعات (-)
    ReturnIn       = 4, // مرتجع مبيعات (+)
    ReturnOut      = 5, // مرتجع مشتريات (-)
    Audit          = 6, // جرد (+/-)
    Adjustment     = 7, // تسوية يدوية (+/-)
    TransferIn     = 8, // تحويل للداخل
    TransferOut    = 9  // تحويل للخارج
}

/// <summary>
/// يمثل سجل حركة المخزون - لتتبع كل صنف (منتج أو موديل) تاريخِيًا
/// </summary>
public class InventoryMovement : BaseEntity
{
    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public InventoryMovementType Type { get; set; }
    public int Quantity { get; set; }       // الكمية (موجب أو سالب)
    public int RemainingStock { get; set; } // الرصيد بعد الحركة

    public string? Reference { get; set; }   // كود الفاتورة أو رقم الجرد
    public string? Note { get; set; }

    public decimal UnitCost { get; set; }    // تكلفة الوحدة وقت الحركة
    public string? CreatedByUserId { get; set; }
}
