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
    
    public List<RevenueChartItemDto> RevenueChartData { get; set; } = new();
    public List<ExpiringTrialDto> ExpiringTrials { get; set; } = new();
    public List<SystemAlertDto> RecentAlerts { get; set; } = new();
}

public class RevenueChartItemDto
{
    public string Name { get; set; } = null!;
    public decimal Value { get; set; }
}

public class ExpiringTrialDto
{
    public string TenantGuid { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int DaysLeft { get; set; }
}

public class SystemAlertDto
{
    public int Id { get; set; }
    public string Text { get; set; } = null!;
    public string Type { get; set; } = null!; // "critical", "warning"
    public string Time { get; set; } = null!;
}
