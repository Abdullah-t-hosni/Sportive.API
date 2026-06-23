using System;

namespace Sportive.API.Models;

public class TenantListDto
{
    public Guid TenantGuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public TenantStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsLocked { get; set; }
    public string? CurrentPlanName { get; set; }
    public DateTime? SubscriptionExpiresAt { get; set; }
    public bool IsTrial { get; set; }
}

public class TenantUsageDto
{
    public int UsersCount { get; set; }
    public int BranchesCount { get; set; }
    public long StorageUsedBytes { get; set; }
    public int PlanLimitUsers { get; set; }
    public int PlanLimitBranches { get; set; }
    public long PlanLimitStorageBytes { get; set; }
}

public class SuperAdminDashboardStatsDto
{
    public int TotalTenants { get; set; }
    public int ActiveTenants { get; set; }
    public int TrialTenants { get; set; }
    public int ExpiredTenants { get; set; }
    public int LockedTenants { get; set; }
}
