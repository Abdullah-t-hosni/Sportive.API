using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Services;
using Sportive.API.Models;
using System.Security.Claims;

namespace Sportive.API.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class AllowPosAccessAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class RequirePermissionAttribute : TypeFilterAttribute
{
    public RequirePermissionAttribute(string module, bool requireEdit = false)
        : base(typeof(RequirePermissionFilter))
    {
        Arguments = new object[] { module, requireEdit };
    }
}

public class RequirePermissionFilter : IAsyncAuthorizationFilter
{
    private readonly string _module;
    private readonly bool _requireEdit;
    private readonly ICacheService _cache;
    private readonly AppDbContext _db;

    public RequirePermissionFilter(string module, bool requireEdit, ICacheService cache, AppDbContext db)
    {
        _module = module;
        _requireEdit = requireEdit;
        _cache = cache;
        _db = db;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity == null || !user.Identity.IsAuthenticated)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Admin has full access to everything
        if (user.IsInRole("Admin") || user.IsInRole("SuperAdmin")) return;

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Fetch permissions from cache (or DB if not cached)
        var cacheKey = $"UserPermissions_{userId}";
        var perms = await _cache.GetOrCreateAsync(cacheKey, async () =>
        {
            return await _db.UserModulePermissions
                .Where(p => p.UserAccountID == userId)
                .Select(p => new { p.ModuleKey, p.CanView, p.CanEdit })
                .ToListAsync();
        }, TimeSpan.FromMinutes(15));

        // â­ï¸ ALLOW POS ACCESS BYPASS
        var hasPosAccessOverride = context.ActionDescriptor.EndpointMetadata.Any(em => em.GetType() == typeof(AllowPosAccessAttribute));
        if (hasPosAccessOverride && perms != null && perms.Any(p => p.ModuleKey == ModuleKeys.Pos && p.CanView))
        {
            return; // Authorized via POS bypass
        }

        var targetPerm = perms?.FirstOrDefault(p => p.ModuleKey == _module);
        
        if (targetPerm == null)
        {
            context.Result = new ForbidResult();
            return;
        }

        if (_requireEdit && !targetPerm.CanEdit)
        {
            context.Result = new ForbidResult();
            return;
        }

        if (!targetPerm.CanView)
        {
            context.Result = new ForbidResult();
            return;
        }
    }
}
