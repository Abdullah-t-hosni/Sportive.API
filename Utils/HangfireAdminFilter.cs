using Hangfire.Dashboard;

using Microsoft.Extensions.DependencyInjection;
using Sportive.API.Services;

namespace Sportive.API.Utils;

public class HangfireAdminFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext.User.Identity?.IsAuthenticated != true)
            return false;

        // Admins bypass
        if (httpContext.User.IsInRole("Admin") || httpContext.User.IsInRole("SuperAdmin"))
            return true;

        var cache = httpContext.RequestServices.GetService<ICacheService>();
        if (cache == null) return false;

        var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return false;

        var perms = cache.GetAsync<List<string>>($"UserPermissions_{userId}").GetAwaiter().GetResult();
        return perms != null && perms.Contains("maintenance");
    }
}
