using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Sportive.API.Utils;

namespace Sportive.API.Models;

public enum TenantStatus : byte
{
    PendingSetup = 0,
    Active = 1,
    Suspended = 2,
    Deleted = 3
}

[Table("Tenants")]
public class Tenant
{
    [Key]
    [Column(TypeName = "char(36)")]
    public Guid TenantGuid { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Subdomain { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? CustomDomain { get; set; }

    [Required]
    [MaxLength(200)]
    public string DatabaseName { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string DatabaseUser { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string DatabasePassword { get; set; } = string.Empty;

    public TenantStatus Status { get; set; } = TenantStatus.Active;

    public DateTime CreatedAt { get; set; } = TimeHelper.GetEgyptTime();

    public DateTime? UpdatedAt { get; set; }

    public bool IsLocked { get; set; }
    
    public DateTime? LockedAt { get; set; }
    
    [MaxLength(500)]
    public string? LockedReason { get; set; }
}
