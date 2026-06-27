using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Sportive.API.Utils;

namespace Sportive.API.Models.System;

public enum InvoiceStatus : byte
{
    Draft = 0,
    Sent = 1,
    Paid = 2,
    Overdue = 3,
    Cancelled = 4
}

[Table("PlatformInvoices")]
public class PlatformInvoice
{
    [Key]
    public int Id { get; set; }

    [Required]
    [Column(TypeName = "char(36)")]
    public Guid TenantGuid { get; set; }

    [Required]
    [MaxLength(50)]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Tax { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public DateTime IssueDate { get; set; } = TimeHelper.GetEgyptTime();

    public DateTime? DueDate { get; set; }

    public DateTime? PaidAt { get; set; }

    [ForeignKey(nameof(TenantGuid))]
    public Tenant? Tenant { get; set; }
}
