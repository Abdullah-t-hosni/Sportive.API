using System;
using System.Collections.Generic;
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

public class SubscriptionService : ISubscriptionService
{
    private readonly MasterDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SubscriptionService> _logger;

    private const string CacheKeyAnalyticsDashboard = "analytics_dashboard";

    public SubscriptionService(MasterDbContext context, IMemoryCache cache, ILogger<SubscriptionService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IEnumerable<SubscriptionDto>> GetAllSubscriptionsAsync()
    {
        var subscriptions = await _context.TenantSubscriptions
            .Include(ts => ts.Plan)
            .Include(ts => ts.Tenant)
            .OrderByDescending(ts => ts.CreatedAt)
            .Select(ts => new SubscriptionDto
            {
                Id = ts.Id,
                TenantGuid = ts.TenantGuid,
                TenantName = ts.Tenant != null ? ts.Tenant.Name : "Unknown",
                PlanId = ts.PlanId,
                PlanName = ts.Plan != null ? ts.Plan.Name : "Unknown",
                Amount = ts.Plan != null ? ts.Plan.MonthlyPrice : 0,
                StartsAt = ts.StartsAt,
                ExpiresAt = ts.ExpiresAt,
                IsActive = ts.IsActive,
                IsTrial = ts.IsTrial,
                TrialEndsAt = ts.TrialEndsAt,
                AutoRenew = ts.AutoRenew,
                GracePeriodDays = ts.GracePeriodDays,
                CreatedAt = ts.CreatedAt,
                UpdatedAt = ts.UpdatedAt
            })
            .ToListAsync();

        return subscriptions;
    }

    public async Task<SubscriptionDto?> GetSubscriptionByIdAsync(int id)
    {
        var ts = await _context.TenantSubscriptions
            .Include(x => x.Plan)
            .Include(x => x.Tenant)
            .FirstOrDefaultAsync(x => x.Id == id);
            
        if (ts == null) return null;

        return new SubscriptionDto
        {
            Id = ts.Id,
            TenantGuid = ts.TenantGuid,
            TenantName = ts.Tenant != null ? ts.Tenant.Name : "Unknown",
            PlanId = ts.PlanId,
            PlanName = ts.Plan != null ? ts.Plan.Name : "Unknown",
            Amount = ts.Plan != null ? ts.Plan.MonthlyPrice : 0,
            StartsAt = ts.StartsAt,
            ExpiresAt = ts.ExpiresAt,
            IsActive = ts.IsActive,
            IsTrial = ts.IsTrial,
            TrialEndsAt = ts.TrialEndsAt,
            AutoRenew = ts.AutoRenew,
            GracePeriodDays = ts.GracePeriodDays,
            CreatedAt = ts.CreatedAt,
            UpdatedAt = ts.UpdatedAt
        };
    }

    public async Task<(bool Success, string Message, SubscriptionDto? Data)> CreateSubscriptionAsync(CreateSubscriptionDto request)
    {
        var tenantExists = await _context.Tenants.AnyAsync(t => t.TenantGuid == request.TenantGuid);
        if (!tenantExists) return (false, "Tenant not found.", null);

        var planExists = await _context.Plans.AnyAsync(p => p.Id == request.PlanId);
        if (!planExists) return (false, "Plan not found.", null);

        var subscription = new TenantSubscription
        {
            TenantGuid = request.TenantGuid,
            PlanId = request.PlanId,
            StartsAt = request.StartsAt,
            ExpiresAt = request.ExpiresAt,
            IsTrial = request.IsTrial,
            TrialEndsAt = request.TrialEndsAt,
            AutoRenew = request.AutoRenew,
            GracePeriodDays = request.GracePeriodDays,
            IsActive = true,
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        _context.TenantSubscriptions.Add(subscription);
        await _context.SaveChangesAsync();

        InvalidateAnalyticsCache();

        var dto = await GetSubscriptionByIdAsync(subscription.Id);
        return (true, "Subscription created successfully.", dto);
    }

    public async Task<(bool Success, string Message, SubscriptionDto? Data)> UpdateSubscriptionAsync(int id, UpdateSubscriptionDto request)
    {
        var subscription = await _context.TenantSubscriptions.Include(x => x.Tenant).FirstOrDefaultAsync(x => x.Id == id);
        if (subscription == null)
            return (false, "Subscription not found.", null);

        if (request.PlanId.HasValue)
        {
            var planExists = await _context.Plans.AnyAsync(p => p.Id == request.PlanId.Value);
            if (!planExists) return (false, "Plan not found.", null);
            subscription.PlanId = request.PlanId.Value;
        }

        if (request.ExpiresAt.HasValue) subscription.ExpiresAt = request.ExpiresAt.Value;
        if (request.IsActive.HasValue) subscription.IsActive = request.IsActive.Value;
        if (request.AutoRenew.HasValue) subscription.AutoRenew = request.AutoRenew.Value;
        if (request.GracePeriodDays.HasValue) subscription.GracePeriodDays = request.GracePeriodDays.Value;

        subscription.UpdatedAt = TimeHelper.GetEgyptTime();

        await _context.SaveChangesAsync();

        InvalidateAnalyticsCache();
        
        if (subscription.Tenant != null)
        {
            _cache.Remove($"tenant_slug_{subscription.Tenant.Slug.ToLowerInvariant()}");
            _cache.Remove($"tenant_subdomain_{subscription.Tenant.Subdomain.ToLowerInvariant()}");
            if (!string.IsNullOrEmpty(subscription.Tenant.CustomDomain))
            {
                _cache.Remove($"tenant_customdomain_{subscription.Tenant.CustomDomain.ToLowerInvariant()}");
            }
        }

        var dto = await GetSubscriptionByIdAsync(subscription.Id);
        return (true, "Subscription updated successfully.", dto);
    }

    private void InvalidateAnalyticsCache()
    {
        _cache.Remove(CacheKeyAnalyticsDashboard);
    }
}
