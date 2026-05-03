using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using System.Security.Claims;

namespace Sportive.API.Attributes;

// ────────────────────────────────────────────────────────────────
// Marker: endpoint is accessible to any user with POS view access
// ────────────────────────────────────────────────────────────────
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class AllowPosAccessAttribute : Attribute { }

// ────────────────────────────────────────────────────────────────
// Declarative attribute — delegates ALL logic to IPermissionService
// Supports comma-separated modules: "Customers,Pos,Orders"
// ────────────────────────────────────────────────────────────────
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class RequirePermissionAttribute : TypeFilterAttribute
{
    public RequirePermissionAttribute(string module, bool requireEdit = false)
        : base(typeof(RequirePermissionFilter))
    {
        Arguments = new object[] { module, requireEdit };
    }
}

// ────────────────────────────────────────────────────────────────
// Filter — thin orchestrator, no business logic
// ────────────────────────────────────────────────────────────────
public class RequirePermissionFilter : IAsyncAuthorizationFilter
{
    private readonly string            _module;
    private readonly bool              _requireEdit;
    private readonly IPermissionService _permissions;
    private readonly ITranslator        _t;

    public RequirePermissionFilter(
        string module, bool requireEdit,
        IPermissionService permissions, ITranslator t)
    {
        _module      = module;
        _requireEdit = requireEdit;
        _permissions = permissions;
        _t           = t;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata;

        // ── 1. AllowAnonymous short-circuit ──────────────────────
        if (metadata.Any(em => em is AllowAnonymousAttribute))
            return;

        // ── 2. Authentication check ──────────────────────────────
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // ── 3. Privileged roles bypass (no DB hit needed) ────────
        if (user.IsInRole(AppRoles.SuperAdmin) || user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.Manager))
            return;

        // ── 4. Resolve user ID ───────────────────────────────────
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // ── 5. POS bypass ────────────────────────────────────────
        if (metadata.Any(em => em is AllowPosAccessAttribute) &&
            await _permissions.HasPosAccessAsync(userId))
            return;

        // ── 6. Module permission check ───────────────────────────
        var modules = _module
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(m => m.Trim());

        if (!await _permissions.CanViewAsync(userId, modules))
        {
            context.Result = new ObjectResult(new { message = _t.Get("Auth.NoViewPermission") })
                { StatusCode = 403 };
            return;
        }

        if (_requireEdit && !await _permissions.CanEditAsync(userId, modules))
        {
            context.Result = new ObjectResult(new { message = _t.Get("Auth.NoEditPermission") })
                { StatusCode = 403 };
        }
    }
}
