using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Sportive.API.Utils;

namespace Sportive.API.Models.System;

public enum TicketStatus : byte
{
    Open = 0,
    InProgress = 1,
    Resolved = 2,
    Closed = 3
}

public enum TicketPriority : byte
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

[Table("SupportTickets")]
public class SupportTicket
{
    [Key]
    public int Id { get; set; }

    [Required]
    [Column(TypeName = "char(36)")]
    public Guid TenantGuid { get; set; }

    [Required]
    [MaxLength(200)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;

    public TicketStatus Status { get; set; } = TicketStatus.Open;

    public TicketPriority Priority { get; set; } = TicketPriority.Medium;

    public DateTime CreatedAt { get; set; } = TimeHelper.GetEgyptTime();

    public DateTime? UpdatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    [MaxLength(100)]
    public string? AssignedToAdminId { get; set; }

    [ForeignKey(nameof(TenantGuid))]
    public Tenant? Tenant { get; set; }
}
