using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using System.Security.Claims;

namespace Sportive.API.Authorization;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequireModulePermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string[] _moduleKeys;
    private readonly bool   _requireEdit;

    public RequireModulePermissionAttribute(string moduleKey, bool requireEdit = false)
    {
        _moduleKeys   = moduleKey.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).ToArray();
        _requireEdit = requireEdit;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Admin و Manager يعدّوا على كل الصلاحيات
        if (user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.Manager))
            return;

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) { context.Result = new ForbidResult(); return; }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

        var perms = await db.UserModulePermissions
            .AsNoTracking()
            .Where(p => p.UserAccountID == userId && _moduleKeys.Contains(p.ModuleKey))
            .ToListAsync();

        if (!perms.Any(p => p.CanView))
        {
            var t = context.HttpContext.RequestServices.GetRequiredService<ITranslator>();
            context.Result = new ObjectResult(new { message = t.Get("Auth.NoViewPermission") })
                { StatusCode = 403 };
            return;
        }

        if (_requireEdit && !perms.Any(p => p.CanView && p.CanEdit))
        {
            var t = context.HttpContext.RequestServices.GetRequiredService<ITranslator>();
            context.Result = new ObjectResult(new { message = t.Get("Auth.NoEditPermission") })
                { StatusCode = 403 };
        }
    }
}
