using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sportive.API.Models;

/// <summary>
/// يمثل جلسة مراجعة وتدقيق الجرد البصري (Checklist بدون تسميع في المخزون الفعلي)
/// </summary>
public class InventoryChecklistSession : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public string? CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }

    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    [Column(TypeName = "LONGTEXT")]
    public string ChecklistJson { get; set; } = "{}";
}
