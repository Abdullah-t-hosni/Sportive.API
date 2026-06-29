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

        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        var isPublicEndpoint = path.StartsWith("/api/public") || 
                               path.StartsWith("/api/system") || 
                               path.StartsWith("/api/auth") || 
                               path.StartsWith("/health") || 
                               path.StartsWith("/swagger") || 
                               path.StartsWith("/uploads");

        // 1. Resolve tenant context for the request
        var tenant = await tenantResolver.ResolveTenantAsync();
        
        if (tenant == null)
        {
            if (isPublicEndpoint)
            {
                await _next(context);
                return;
            }

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

        // 3. Subscription Expiry & Grace Period Check
        path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        var isExemptedPath = path.StartsWith("/api/auth") || path.StartsWith("/api/settings") || path.StartsWith("/api/system");

        if (!isExemptedPath && tenant.ActiveSubscriptionExpiresAt.HasValue)
        {
            var expiryWithGrace = tenant.ActiveSubscriptionExpiresAt.Value.AddDays(tenant.ActiveSubscriptionGraceDays);
            if (Sportive.API.Utils.TimeHelper.GetEgyptTime() > expiryWithGrace)
            {
                var method = context.Request.Method;
                if (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method))
                {
                    context.Response.StatusCode = 402; // Payment Required
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        success = false,
                        errorCode = "SUBSCRIPTION_EXPIRED",
                        message = "Tenant subscription has expired and grace period ended."
                    }));
                    return;
                }
            }
        }

        // The JWT is now manually parsed in TenantResolver (Step 1) and has the highest priority.
        // Therefore, it is impossible for an authenticated user to bypass their assigned tenant via Headers or Subdomain.

        await _next(context);
    }
}
