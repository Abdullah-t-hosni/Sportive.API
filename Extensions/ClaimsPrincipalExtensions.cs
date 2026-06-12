using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sportive.API.Data;
using Sportive.API.Models;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace Sportive.API.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static int? GetBranchId(this ClaimsPrincipal user)
    {
        var branchIdStr = user.FindFirst("BranchId")?.Value;
        if (int.TryParse(branchIdStr, out var branchId))
        {
            return branchId;
        }
        return null;
    }

    public static int? GetWarehouseId(this ClaimsPrincipal user)
    {
        var warehouseIdStr = user.FindFirst("WarehouseId")?.Value;
        if (int.TryParse(warehouseIdStr, out var warehouseId))
        {
            return warehouseId;
        }
        return null;
    }

    public static async Task<bool> HasViewAllBranchesAsync(this ClaimsPrincipal user, HttpContext context)
    {
        if (user.IsInRole(AppRoles.SuperAdmin) || user.IsInRole(AppRoles.Admin))
            return true;

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return false;

        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        
        // Check database for explicit permission
        var perm = await db.UserModulePermissions
            .FirstOrDefaultAsync(p => p.UserAccountID == userId && p.ModuleKey == ModuleKeys.ViewAllBranches);
            
        if (perm != null && perm.CanView)
            return true;
            
        return false;
    }
}
