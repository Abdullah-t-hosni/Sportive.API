using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public interface ITenantResolver
{
    Task<Tenant?> ResolveTenantAsync();
}

public class TenantResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantRegistry _tenantRegistry;

    public TenantResolver(IHttpContextAccessor httpContextAccessor, ITenantRegistry tenantRegistry)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantRegistry = tenantRegistry;
    }

    public async Task<Tenant?> ResolveTenantAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return null;

        // 1. Resolve from JWT claims (highest priority and trusted)
        if (httpContext.User?.Identity?.IsAuthenticated == true)
        {
            var tenantClaim = httpContext.User.FindFirst("tenantId")?.Value 
                ?? httpContext.User.FindFirst("TenantId")?.Value;
            if (!string.IsNullOrWhiteSpace(tenantClaim))
            {
                var tenant = await _tenantRegistry.GetTenantBySlugAsync(tenantClaim);
                if (tenant != null) return tenant;
            }
        }

        // 2. Resolve from X-Tenant-Id Header
        if (httpContext.Request.Headers.TryGetValue("X-Tenant-Id", out var headerValues))
        {
            var headerTenantId = headerValues.ToString();
            if (!string.IsNullOrWhiteSpace(headerTenantId))
            {
                var tenant = await _tenantRegistry.GetTenantBySlugAsync(headerTenantId);
                if (tenant != null) return tenant;
            }
        }

        // 3. Resolve from Custom Domain
        var host = httpContext.Request.Host.Host;
        if (!string.IsNullOrWhiteSpace(host) && !host.Contains("localhost"))
        {
            var tenantByDomain = await _tenantRegistry.GetTenantByCustomDomainAsync(host);
            if (tenantByDomain != null) return tenantByDomain;
        }

        // 4. Resolve from Subdomain
        var subdomain = GetSubdomainFromHost(host);
        if (!string.IsNullOrWhiteSpace(subdomain) && subdomain != "www" && subdomain != "api" && !subdomain.Contains("localhost"))
        {
            var tenant = await _tenantRegistry.GetTenantBySubdomainAsync(subdomain);
            if (tenant != null) return tenant;
        }

        // 5. Hardcoded fallback for existing frontend legacy domains
        if (host.Contains("sportive-sportwear.com"))
        {
            return await _tenantRegistry.GetTenantBySlugAsync("sportive");
        }

        // 6. No default fallback (forces explicit header/JWT/subdomain context)
        return null;
    }

    private string? GetSubdomainFromHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;

        // E.g. borslin.raakiza.com -> "borslin"
        // If the host is an IP address, ignore subdomain parsing
        if (System.Net.IPAddress.TryParse(host, out _))
        {
            return null;
        }

        var parts = host.Split('.');
        if (parts.Length > 2)
        {
            return parts[0];
        }

        return null;
    }
}
