using System;
using System.ComponentModel.DataAnnotations;

namespace Sportive.API.DTOs.System;

public class SubscriptionDto
{
    public int Id { get; set; }
    public Guid TenantGuid { get; set; }
    public int PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public bool IsTrial { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public bool AutoRenew { get; set; }
    public int GracePeriodDays { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateSubscriptionDto
{
    [Required]
    public Guid TenantGuid { get; set; }

    [Required]
    public int PlanId { get; set; }

    [Required]
    public DateTime StartsAt { get; set; }

    [Required]
    public DateTime ExpiresAt { get; set; }

    public bool IsTrial { get; set; } = false;
    public DateTime? TrialEndsAt { get; set; }
    public bool AutoRenew { get; set; } = false;
    public int GracePeriodDays { get; set; } = 7;
}

public class UpdateSubscriptionDto
{
    public int? PlanId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool? IsActive { get; set; }
    public bool? AutoRenew { get; set; }
    public int? GracePeriodDays { get; set; }
}
