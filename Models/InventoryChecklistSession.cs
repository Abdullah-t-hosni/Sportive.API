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

    public DateTime StartedAt { get; set; } = Sportive.API.Utils.TimeHelper.GetEgyptTime();
    public DateTime? ClosedAt { get; set; }
    public string? ClosedByUserId { get; set; }
    public string? ClosedByName { get; set; }

    public int VerifiedCount { get; set; } = 0;
    public int IssueCount { get; set; } = 0;
    public int TotalItemsCount { get; set; } = 0;

    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    [Column(TypeName = "LONGTEXT")]
    public string ChecklistJson { get; set; } = "{}";
}
