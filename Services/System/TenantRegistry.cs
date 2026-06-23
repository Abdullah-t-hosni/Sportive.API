using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public class TenantRegistry : ITenantRegistry
{
    private readonly MasterDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan NegCacheDuration = TimeSpan.FromMinutes(5); // Cache negative results to prevent DB spamming

    public TenantRegistry(MasterDbContext dbContext, IMemoryCache cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<Tenant?> GetTenantBySlugAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        
        var cacheKey = $"tenant_slug_{slug.ToLowerInvariant()}";
        
        if (_cache.TryGetValue(cacheKey, out Tenant? cachedTenant))
        {
            return cachedTenant;
        }

        var tenant = await _dbContext.Tenants
            .FirstOrDefaultAsync(t => t.Slug == slug);

        if (tenant != null)
        {
            var sub = await _dbContext.TenantSubscriptions
                .Where(s => s.TenantGuid == tenant.TenantGuid && s.IsActive)
                .OrderByDescending(s => s.ExpiresAt)
                .FirstOrDefaultAsync();
                
            if (sub != null)
            {
                tenant.ActiveSubscriptionExpiresAt = sub.ExpiresAt;
                tenant.ActiveSubscriptionGraceDays = sub.GracePeriodDays;
            }
        }

        var cacheOptions = new MemoryCacheEntryOptions
        {
            SlidingExpiration = tenant != null ? CacheDuration : NegCacheDuration
        };

        _cache.Set(cacheKey, tenant, cacheOptions);

        return tenant;
    }

    public async Task<Tenant?> GetTenantBySubdomainAsync(string subdomain)
    {
        if (string.IsNullOrWhiteSpace(subdomain)) return null;

        var cacheKey = $"tenant_subdomain_{subdomain.ToLowerInvariant()}";

        if (_cache.TryGetValue(cacheKey, out Tenant? cachedTenant))
        {
            return cachedTenant;
        }

        var tenant = await _dbContext.Tenants
            .FirstOrDefaultAsync(t => t.Subdomain == subdomain);

        if (tenant != null)
        {
            var sub = await _dbContext.TenantSubscriptions
                .Where(s => s.TenantGuid == tenant.TenantGuid && s.IsActive)
                .OrderByDescending(s => s.ExpiresAt)
                .FirstOrDefaultAsync();
                
            if (sub != null)
            {
                tenant.ActiveSubscriptionExpiresAt = sub.ExpiresAt;
                tenant.ActiveSubscriptionGraceDays = sub.GracePeriodDays;
            }
        }

        var cacheOptions = new MemoryCacheEntryOptions
        {
            SlidingExpiration = tenant != null ? CacheDuration : NegCacheDuration
        };

        _cache.Set(cacheKey, tenant, cacheOptions);

        return tenant;
    }

    public async Task<Tenant?> GetTenantByCustomDomainAsync(string customDomain)
    {
        if (string.IsNullOrWhiteSpace(customDomain)) return null;

        var cacheKey = $"tenant_customdomain_{customDomain.ToLowerInvariant()}";

        if (_cache.TryGetValue(cacheKey, out Tenant? cachedTenant))
        {
            return cachedTenant;
        }

        var tenant = await _dbContext.Tenants
            .FirstOrDefaultAsync(t => t.CustomDomain == customDomain);

        if (tenant != null)
        {
            var sub = await _dbContext.TenantSubscriptions
                .Where(s => s.TenantGuid == tenant.TenantGuid && s.IsActive)
                .OrderByDescending(s => s.ExpiresAt)
                .FirstOrDefaultAsync();
                
            if (sub != null)
            {
                tenant.ActiveSubscriptionExpiresAt = sub.ExpiresAt;
                tenant.ActiveSubscriptionGraceDays = sub.GracePeriodDays;
            }
        }

        var cacheOptions = new MemoryCacheEntryOptions
        {
            SlidingExpiration = tenant != null ? CacheDuration : NegCacheDuration
        };

        _cache.Set(cacheKey, tenant, cacheOptions);

        return tenant;
    }
}
