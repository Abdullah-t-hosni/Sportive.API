using Hangfire.Dashboard;

namespace Sportive.API.Utils;

public class HangfireAdminFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        // 🔒 Only allow Admins/SuperAdmins to see the dashboard
        return httpContext.User.Identity?.IsAuthenticated == true && 
               (httpContext.User.IsInRole("Admin") || httpContext.User.IsInRole("SuperAdmin"));
    }
}
