namespace Sportive.API.DTOs.System;

public class SuperAdminDashboardStatsDto
{
    public int TotalTenants { get; set; }
    public int ActiveTenants { get; set; }
    public int LockedTenants { get; set; }
    public int ExpiredTenants { get; set; }
    public int TrialTenants { get; set; }
    public int ActiveSubscriptions { get; set; }
    public int ExpiredSubscriptions { get; set; }
    public decimal EstimatedMonthlyRevenue { get; set; }
}
