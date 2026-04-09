using Sportive.API.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager")]
public class StaffController : ControllerBase
{
    private readonly UserManager<AppUser>     _users;
    private readonly RoleManager<IdentityRole>_roles;
    private readonly AppDbContext             _db;

    public StaffController(
        UserManager<AppUser>     users,
        RoleManager<IdentityRole>roles,
        AppDbContext             db)
    {
        _users = users;
        _roles = roles;
        _db    = db;
    }

    // ══════════════════════════════════════════════════
    // GET /api/staff
    // قائمة كل الموظفين (بدون الـ Customers)
    // ══════════════════════════════════════════════════
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var staffRoles = AppRoles.StaffRoles.ToList();

        // جلب كل يوزر عنده دور موظف
        var staffUsers = new List<object>();
        foreach (var user in await _users.Users.Where(u => u.IsActive || !u.IsActive).ToListAsync())
        {
            var userRoles = await _users.GetRolesAsync(user);
            if (!userRoles.Any(r => staffRoles.Contains(r) && r != AppRoles.Customer))
                continue;

            staffUsers.Add(new {
                id          = user.Id,
                fullName    = user.FullName,
                email       = user.Email,
                phone       = user.PhoneNumber,
                isActive    = user.IsActive,
                roles       = userRoles.Where(r => r != AppRoles.Customer).ToList(),
                primaryRole = GetPrimaryRole(userRoles),
                createdAt   = user.CreatedAt,
            });
        }

        return Ok(staffUsers.OrderBy(s => ((dynamic)s).primaryRole));
    }

    // ══════════════════════════════════════════════════
    // POST /api/staff
    // إنشاء موظف جديد
    // ══════════════════════════════════════════════════
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStaffDto dto)
    {
        // تحقق من صحة الدور
        var validRole = AppRoles.StaffRoles.FirstOrDefault(r => r.Equals(dto.Role, StringComparison.OrdinalIgnoreCase));
        if (validRole == null)
            return BadRequest(new { message = $"دور غير صالح: {dto.Role}" });
        dto = dto with { Role = validRole };

        // تحقق من التكرار
        if (await _users.FindByEmailAsync(dto.Email) != null)
            return BadRequest(new { message = "الإيميل مستخدم بالفعل" });

        var phoneExists = await _users.Users.AnyAsync(u => u.PhoneNumber == dto.Phone);
        if (phoneExists)
            return BadRequest(new { message = "رقم التليفون مستخدم بالفعل" });

        // إنشاء الأدوار لو مش موجودة
        await EnsureRolesAsync();

        var user = new AppUser
        {
            UserName    = dto.Email,
            Email       = dto.Email,
            FullName    = dto.FullName,
            PhoneNumber = dto.Phone,
            IsActive    = true,
            CreatedAt   = TimeHelper.GetEgyptTime(),
        };

        var result = await _users.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        await _users.AddToRoleAsync(user, dto.Role);

        return Ok(new {
            id       = user.Id,
            fullName = user.FullName,
            role     = dto.Role,
            message  = $"تم إنشاء {GetRoleAr(dto.Role)} بنجاح"
        });
    }

    // ══════════════════════════════════════════════════
    // PUT /api/staff/{id}/role
    // تغيير دور موظف
    // ══════════════════════════════════════════════════
    [HttpPut("{id}/role")]
    public async Task<IActionResult> ChangeRole(string id, [FromBody] ChangeRoleDto dto)
    {
        var validRole = AppRoles.StaffRoles.FirstOrDefault(r => r.Equals(dto.Role, StringComparison.OrdinalIgnoreCase));
        if (validRole == null)
            return BadRequest(new { message = $"دور غير صالح: {dto.Role}" });
        dto = dto with { Role = validRole };

        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        // منع تغيير دور الأدمن الأصلي
        if (user.Email == "admin@sportive.com" && dto.Role != AppRoles.Admin)
            return BadRequest(new { message = "لا يمكن تغيير دور الأدمن الأصلي" });

        var currentRoles = await _users.GetRolesAsync(user);
        var staffRoles   = currentRoles.Where(r => r != AppRoles.Customer).ToList();

        // احذف الأدوار القديمة وأضف الجديد
        if (staffRoles.Any())
            await _users.RemoveFromRolesAsync(user, staffRoles);
        await _users.AddToRoleAsync(user, dto.Role);

        return Ok(new {
            id,
            newRole  = dto.Role,
            roleAr   = GetRoleAr(dto.Role),
            message  = "تم تغيير الدور بنجاح"
        });
    }

    // ══════════════════════════════════════════════════
    // PATCH /api/staff/{id}/toggle-active
    // تفعيل / تعطيل الموظف
    // ══════════════════════════════════════════════════
    [HttpPatch("{id}/toggle-active")]
    public async Task<IActionResult> ToggleActive(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();
        if (user.Email == "admin@sportive.com")
            return BadRequest(new { message = "لا يمكن تعطيل الأدمن الأصلي" });

        user.IsActive = !user.IsActive;
        await _users.UpdateAsync(user);

        return Ok(new { isActive = user.IsActive });
    }

    // ══════════════════════════════════════════════════
    // PUT /api/staff/{id}/reset-password
    // إعادة تعيين كلمة مرور موظف
    // ══════════════════════════════════════════════════
    [HttpPut("{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] StaffResetPasswordDto dto)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        var token  = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, token, dto.NewPassword);

        return result.Succeeded
            ? Ok(new { message = "تم تغيير كلمة المرور بنجاح" })
            : BadRequest(new { errors = result.Errors.Select(e => e.Description) });
    }

    // ══════════════════════════════════════════════════
    // DELETE /api/staff/{id}
    // حذف موظف نهائياً أو تعطيله
    // ══════════════════════════════════════════════════
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();
        if (user.Email == "admin@sportive.com")
            return BadRequest(new { message = "لا يمكن حذف الأدمن الأصلي" });

        // نفضل التعطيل (Soft Delete) لو كان هناك سجلات مرتبطة به
        var hasOrders = await _db.Orders.AnyAsync(o => o.SalesPersonId == id);
        if (hasOrders)
        {
            user.IsActive = false;
            await _users.UpdateAsync(user);
            return Ok(new { message = "تم تعطيل الحساب بدلاً من الحذف لوجود سجلات مبيعات مرتبطة به" });
        }

        var result = await _users.DeleteAsync(user);
        return result.Succeeded
            ? Ok(new { message = "تم حذف الموظف بنجاح" })
            : BadRequest(new { errors = result.Errors.Select(e => e.Description) });
    }

    // ══════════════════════════════════════════════════
    // GET /api/staff/{id}/permissions
    // جلب صلاحيات المستخدم الخاصة
    // ══════════════════════════════════════════════════
    [HttpGet("{id}/permissions")]
    public async Task<IActionResult> GetPermissions(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        var perms = await _db.UserModulePermissions
            .Where(p => p.UserAccountID == id)
            .Select(p => new { p.ModuleKey, p.CanView, p.CanEdit })
            .ToListAsync();

        return Ok(perms);
    }

    // ══════════════════════════════════════════════════
    // PUT /api/staff/{id}/permissions
    // تحديث صلاحيات المستخدم (الحق فى الرؤية أو التحكم)
    // ══════════════════════════════════════════════════
    [HttpPut("{id}/permissions")]
    public async Task<IActionResult> UpdatePermissions(string id, [FromBody] List<UserModulePermissionDto> dto)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        // حذف القديم
        var existing = await _db.UserModulePermissions.Where(p => p.UserAccountID == id).ToListAsync();
        _db.UserModulePermissions.RemoveRange(existing);

        // إضافة الجديد
        foreach (var p in dto)
        {
            _db.UserModulePermissions.Add(new UserModulePermission
            {
                UserAccountID = id,
                ModuleKey     = p.ModuleKey,
                CanView       = p.CanView,
                CanEdit       = p.CanEdit
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "تم تحديث الصلاحيات بنجاح" });
    }

    // ══════════════════════════════════════════════════
    // GET /api/staff/roles
    // قائمة الأدوار المتاحة
    // ══════════════════════════════════════════════════
    [HttpGet("roles")]
    public IActionResult GetRoles()
    {
        return Ok(new[]
        {
            new { value = AppRoles.Admin,      labelAr = "مدير النظام",      labelEn = "System Admin",  permissions = "كل الصلاحيات" },
            new { value = AppRoles.Manager,    labelAr = "مدير الفرع",       labelEn = "Branch Manager", permissions = "كل شيء ما عدا إعدادات النظام" },
            new { value = AppRoles.Cashier,    labelAr = "كاشير",            labelEn = "Cashier",        permissions = "POS فقط" },
            new { value = AppRoles.Accountant, labelAr = "محاسب",            labelEn = "Accountant",     permissions = "التقارير المالية والمحاسبة" },
            new { value = AppRoles.Staff,      labelAr = "موظف",             labelEn = "Staff",          permissions = "الطلبات والمنتجات (عرض)" },
        });
    }

    // ── Helpers ───────────────────────────────────────
    private async Task EnsureRolesAsync()
    {
        foreach (var role in AppRoles.StaffRoles)
            if (!await _roles.RoleExistsAsync(role))
                await _roles.CreateAsync(new IdentityRole(role));
    }

    private static string GetPrimaryRole(IList<string> roles)
    {
        foreach (var r in new[] { AppRoles.Admin, AppRoles.Manager, AppRoles.Accountant, AppRoles.Cashier, AppRoles.Staff })
            if (roles.Contains(r)) return r;
        return AppRoles.Customer;
    }

    private static string GetRoleAr(string role) => role switch
    {
        AppRoles.Admin      => "مدير النظام",
        AppRoles.Manager    => "مدير الفرع",
        AppRoles.Cashier    => "كاشير",
        AppRoles.Accountant => "محاسب",
        AppRoles.Staff      => "موظف",
        _                   => role
    };
}

// ── DTOs ──────────────────────────────────────────────
public record CreateStaffDto(
    string FullName,
    string Email,
    string Phone,
    string Password,
    string Role
);

public record ChangeRoleDto(string Role);
public record StaffResetPasswordDto(string NewPassword);
public record UserModulePermissionDto(string ModuleKey, bool CanView, bool CanEdit);
