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
    private readonly Microsoft.Extensions.Logging.ILogger<TenantResolver> _logger;

    public TenantResolver(
        IHttpContextAccessor httpContextAccessor, 
        ITenantRegistry tenantRegistry,
        Microsoft.Extensions.Logging.ILogger<TenantResolver> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantRegistry = tenantRegistry;
        _logger = logger;
    }

    public async Task<Tenant?> ResolveTenantAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return null;

        // 1. Resolve from JWT claims (highest priority and trusted)
        var authHeader = httpContext.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", System.StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader.Substring("Bearer ".Length).Trim();
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            if (handler.CanReadToken(token))
            {
                var jwt = handler.ReadJwtToken(token);
                var tenantClaim = jwt.Claims.FirstOrDefault(c => c.Type == "tenantId" || c.Type == "TenantId")?.Value;
                if (!string.IsNullOrWhiteSpace(tenantClaim))
                {
                    var tenant = await _tenantRegistry.GetTenantBySlugAsync(tenantClaim);
                    if (tenant != null) return tenant;
                }
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
        _logger.LogInformation("Resolving tenant for host: {Host}", host);

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
        if (host.Contains("sportive-sportwear.com", StringComparison.OrdinalIgnoreCase))
        {
            return await _tenantRegistry.GetTenantBySlugAsync("sportive");
        }

        // TEMPORARY PRODUCTION FALLBACK
        _logger.LogWarning("TEMPORARY FALLBACK: Unrecognized host '{Host}'. Defaulting to 'sportive' tenant.", host);
        return await _tenantRegistry.GetTenantBySlugAsync("sportive");
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
