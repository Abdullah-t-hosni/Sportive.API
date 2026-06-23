using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.DTOs.System;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services;

public class AnalyticsService : IAnalyticsService
{
    private readonly MasterDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AnalyticsService> _logger;

    private const string CacheKeyAnalyticsDashboard = "analytics_dashboard";

    public AnalyticsService(MasterDbContext context, IMemoryCache cache, ILogger<AnalyticsService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<SuperAdminDashboardStatsDto> GetDashboardStatsAsync()
    {
        if (_cache.TryGetValue(CacheKeyAnalyticsDashboard, out SuperAdminDashboardStatsDto? cachedStats))
        {
            return cachedStats!;
        }

        var tenants = await _context.Tenants.ToListAsync();
        var subs = await _context.TenantSubscriptions
            .Include(s => s.Plan)
            .ToListAsync();

        var now = TimeHelper.GetEgyptTime();

        var total = tenants.Count;
        var active = tenants.Count(t => t.Status == TenantStatus.Active && !t.IsLocked);
        var locked = tenants.Count(t => t.IsLocked || t.Status == TenantStatus.Suspended);
        
        var trialTenantsCount = subs.Count(s => s.IsActive && s.IsTrial && s.ExpiresAt > now);
        
        // Find tenants whose latest subscription is expired
        var latestSubs = subs.GroupBy(s => s.TenantGuid)
            .Select(g => g.OrderByDescending(s => s.CreatedAt).First())
            .ToList();
            
        var expiredTenantsCount = latestSubs.Count(s => s.ExpiresAt < now);

        var activeSubs = subs.Where(s => s.IsActive && s.ExpiresAt > now).ToList();
        var expiredSubs = subs.Count(s => s.ExpiresAt < now);

        var estimatedRevenue = activeSubs
            .Where(s => !s.IsTrial && s.Plan != null)
            .Sum(s => s.Plan!.MonthlyPrice);

        var stats = new SuperAdminDashboardStatsDto
        {
            TotalTenants = total,
            ActiveTenants = active,
            LockedTenants = locked,
            TrialTenants = trialTenantsCount,
            ExpiredTenants = expiredTenantsCount,
            ActiveSubscriptions = activeSubs.Count,
            ExpiredSubscriptions = expiredSubs,
            EstimatedMonthlyRevenue = estimatedRevenue
        };

        _cache.Set(CacheKeyAnalyticsDashboard, stats, TimeSpan.FromMinutes(5));

        return stats;
    }
}
