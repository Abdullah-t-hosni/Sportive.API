using System;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sportive.API.Interfaces;
using Sportive.API.Services;

namespace Sportive.API.Middleware;

public class TenantValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantValidationMiddleware> _logger;

    public TenantValidationMiddleware(RequestDelegate next, ILogger<TenantValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var tenantResolver = context.RequestServices.GetRequiredService<ITenantResolver>();
        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();

        // 1. Resolve tenant context for the request
        var tenant = await tenantResolver.ResolveTenantAsync();
        if (tenant == null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                success = false,
                message = "Unable to resolve tenant context."
            }));
            return;
        }

        // 2. Store the tenant in scoped context
        tenantContext.SetTenant(tenant);

        // 3. If authenticated, enforce strict match with JWT claim
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var jwtTenantId = context.User.FindFirst("tenantId")?.Value 
                ?? context.User.FindFirst("TenantId")?.Value;

            if (!string.IsNullOrWhiteSpace(jwtTenantId))
            {
                if (!string.Equals(jwtTenantId, tenant.Slug, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Security Violation: Tenant mismatch. JWT Tenant: '{JwtTenant}', Resolved Tenant: '{ResolvedTenant}' for path '{Path}'", 
                        jwtTenantId, tenant.Slug, context.Request.Path);

                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        message = "Access denied: Tenant context mismatch."
                    }));
                    return;
                }
            }
        }

        await _next(context);
    }
}
