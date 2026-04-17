using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services;

public class StaffPermissionService
{
    private readonly AppDbContext _db;

    public StaffPermissionService(AppDbContext db) => _db = db;

    /// <summary>
    /// يحذف صلاحيات المستخدم الحالية ويزرع الافتراضية بناءً على الدور.
    /// يُستدعى عند إنشاء المستخدم أو تغيير دوره.
    /// </summary>
    public async Task SeedDefaultPermissionsAsync(string userId, string role)
    {
        var existing = await _db.UserModulePermissions
            .Where(p => p.UserAccountID == userId)
            .ToListAsync();
        _db.UserModulePermissions.RemoveRange(existing);

        var defaults = DefaultPermissions.ForRole(role);
        var now = TimeHelper.GetEgyptTime();

        foreach (var (moduleKey, canView, canEdit) in defaults)
        {
            _db.UserModulePermissions.Add(new UserModulePermission
            {
                UserAccountID = userId,
                ModuleKey     = moduleKey,
                CanView       = canView,
                CanEdit       = canEdit,
                CreatedAt     = now,
            });
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Backfill للمستخدمين الموجودين اللي مفيش عندهم صلاحيات في الـ DB.
    /// </summary>
    public async Task BackfillMissingPermissionsAsync(
        Microsoft.AspNetCore.Identity.UserManager<AppUser> userManager)
    {
        var users = await userManager.Users
            .Where(u => u.IsActive)
            .ToListAsync();

        foreach (var user in users)
        {
            var hasPerms = await _db.UserModulePermissions
                .AnyAsync(p => p.UserAccountID == user.Id);
            if (hasPerms) continue;

            var roles = await userManager.GetRolesAsync(user);
            var primaryRole = GetPrimaryRole(roles);
            if (primaryRole == AppRoles.Customer) continue;

            await SeedDefaultPermissionsAsync(user.Id, primaryRole);
        }
    }

    private static string GetPrimaryRole(IList<string> roles)
    {
        foreach (var r in new[] { AppRoles.Admin, AppRoles.Manager, AppRoles.Accountant, AppRoles.Cashier, AppRoles.Staff })
            if (roles.Contains(r)) return r;
        return AppRoles.Customer;
    }
}
