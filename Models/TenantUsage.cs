using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sportive.API.Models;

[Table("TenantUsages")]
public class TenantUsage
{
    [Key]
    public int Id { get; set; }

    [Required]
    public Guid TenantGuid { get; set; }

    public long StorageUsedBytes { get; set; }

    public DateTime LastCalculatedAt { get; set; }

    // Navigation property
    public Tenant? Tenant { get; set; }
}
