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
    private readonly string _moduleKey;
    private readonly bool   _requireEdit;

    public RequireModulePermissionAttribute(string moduleKey, bool requireEdit = false)
    {
        _moduleKey   = moduleKey;
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

        var perm = await db.UserModulePermissions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserAccountID == userId && p.ModuleKey == _moduleKey);

        if (perm == null || !perm.CanView)
        {
            var t = context.HttpContext.RequestServices.GetRequiredService<ITranslator>();
            context.Result = new ObjectResult(new { message = t.Get("Auth.NoViewPermission") })
                { StatusCode = 403 };
            return;
        }

        if (_requireEdit && !perm.CanEdit)
        {
            var t = context.HttpContext.RequestServices.GetRequiredService<ITranslator>();
            context.Result = new ObjectResult(new { message = t.Get("Auth.NoEditPermission") })
                { StatusCode = 403 };
        }
    }
}
