using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Sportive.API.Utils;

namespace Sportive.API.Models;

[Table("TenantSubscriptions")]
public class TenantSubscription
{
    [Key]
    public int Id { get; set; }

    [Required]
    public Guid TenantGuid { get; set; }

    [Required]
    public int PlanId { get; set; }

    public DateTime StartsAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsTrial { get; set; } = false;
    public DateTime? TrialEndsAt { get; set; }
    
    public bool AutoRenew { get; set; } = false;
    public int GracePeriodDays { get; set; } = 7;

    public DateTime CreatedAt { get; set; } = TimeHelper.GetEgyptTime();
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Plan? Plan { get; set; }
}
