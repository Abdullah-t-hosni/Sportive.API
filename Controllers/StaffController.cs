using Sportive.API.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager")]
public class StaffController : ControllerBase
{
    private readonly UserManager<AppUser>      _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly AppDbContext              _db;
    private readonly StaffPermissionService    _permService;
    private readonly SequenceService           _sequence;
    private readonly ICustomerService          _customerService;

    public StaffController(
        UserManager<AppUser>      users,
        RoleManager<IdentityRole> roles,
        AppDbContext              db,
        StaffPermissionService    permService,
        SequenceService           sequence,
        ICustomerService          customerService)
    {
        _users           = users;
        _roles           = roles;
        _db              = db;
        _permService     = permService;
        _sequence        = sequence;
        _customerService = customerService;
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
        var staffUsers    = new List<object>();
        var employeeLinks = await _db.Employees
            .Where(e => e.AppUserId != null)
            .Select(e => new { e.AppUserId, e.Id, e.EmployeeNumber })
            .ToListAsync();

        foreach (var user in await _users.Users.ToListAsync())
        {
            var userRoles = await _users.GetRolesAsync(user);
            if (!userRoles.Any(r => staffRoles.Contains(r) && r != AppRoles.Customer))
                continue;

            var link = employeeLinks.FirstOrDefault(e => e.AppUserId == user.Id);

            staffUsers.Add(new {
                id             = user.Id,
                fullName       = user.FullName,
                email          = user.Email,
                phone          = user.PhoneNumber,
                isActive       = user.IsActive,
                roles          = userRoles.Where(r => r != AppRoles.Customer).ToList(),
                primaryRole    = GetPrimaryRole(userRoles),
                createdAt      = user.CreatedAt,
                employeeId     = link?.Id,
                employeeNumber = link?.EmployeeNumber,
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

        // زرع الصلاحيات الافتراضية بناءً على الدور
        await _permService.SeedDefaultPermissionsAsync(user.Id, dto.Role);

        // ✅ ربط أو إنشاء سجل HR وتحضير الحسابات المحاسبية
        await EnsureEmployeeLinkAsync(user, dto.Role);

        return Ok(new {
            id       = user.Id,
            fullName = user.FullName,
            role     = dto.Role,
            message  = $"تم إنشاء {GetRoleAr(dto.Role)} بنجاح ومزامنته مع الـ HR"
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
        
        var result = await _users.AddToRoleAsync(user, dto.Role);
        if (!result.Succeeded)
            return BadRequest(new { message = "فشل في تغيير الدور: " + result.Errors.FirstOrDefault()?.Description });

        // إعادة زرع الصلاحيات الافتراضية للدور الجديد
        await _permService.SeedDefaultPermissionsAsync(user.Id, dto.Role);

        // ✅ ربط أو تحديث سجل HR
        await EnsureEmployeeLinkAsync(user, dto.Role);

        return Ok(new {
            id,
            newRole  = dto.Role,
            roleAr   = GetRoleAr(dto.Role),
            message  = "تم تغيير الدور وتحديث سجل الموارد البشرية بنجاح"
        });
    }

    private async Task EnsureEmployeeLinkAsync(AppUser user, string role)
    {
        // 1. ابحث عن موظف مرتبط بهذا المستخدم
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.AppUserId == user.Id);
        
        if (employee == null)
        {
            // 2. إذا لم يوجد، ابحث عن موظف بنفس الهاتف وغير مرتبط بمستخدم (حالة استيراد بيانات قديمة)
            employee = await _db.Employees.FirstOrDefaultAsync(e => e.Phone == user.PhoneNumber && (e.AppUserId == null || e.AppUserId == ""));
            
            if (employee != null)
            {
                employee.AppUserId = user.Id;
            }
            else
            {
                // 3. إنشاء سجل جديد
                var empNo = await _sequence.NextAsync("EMP", async (db, pattern) =>
                {
                    var max = await db.Employees
                        .Where(e => EF.Functions.Like(e.EmployeeNumber, pattern))
                        .Select(e => e.EmployeeNumber).ToListAsync();
                    return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                              .DefaultIfEmpty(0).Max();
                });

                employee = new Employee
                {
                    EmployeeNumber = empNo,
                    Name = user.FullName,
                    Email = user.Email,
                    Phone = user.PhoneNumber,
                    AppUserId = user.Id,
                    JobTitle = role,
                    HireDate = TimeHelper.GetEgyptTime(),
                    Status = EmployeeStatus.Active,
                    CreatedAt = TimeHelper.GetEgyptTime()
                };
                _db.Employees.Add(employee);
            }
        }
        
        // تحديث البيانات الأساسية لضمان التطابق
        employee.Name = user.FullName;
        employee.Email = user.Email;
        employee.Phone = user.PhoneNumber;
        employee.JobTitle = role; // المسمى الوظيفي يتبع الدور

        await _db.SaveChangesAsync();
        await _customerService.EnsureCustomerAccountAsync(0, isEmployee: true, employeeId: employee.Id);
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

        // فك الربط مع سجل الـ HR (مع الحفاظ على السجل)
        var linkedEmployee = await _db.Employees.FirstOrDefaultAsync(e => e.AppUserId == id);
        if (linkedEmployee != null)
        {
            linkedEmployee.AppUserId = null;
            linkedEmployee.UpdatedAt = TimeHelper.GetEgyptTime();
        }

        // حذف الصلاحيات
        var perms = await _db.UserModulePermissions.Where(p => p.UserAccountID == id).ToListAsync();
        _db.UserModulePermissions.RemoveRange(perms);
        await _db.SaveChangesAsync();

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
    // GET /api/staff/{id}/profile
    // بروفايل كامل: بيانات المستخدم + HR + الصلاحيات
    // ══════════════════════════════════════════════════
    [HttpGet("{id}/profile")]
    public async Task<IActionResult> GetProfile(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        var roles = await _users.GetRolesAsync(user);

        var employee = await _db.Employees
            .Where(e => e.AppUserId == id)
            .Select(e => new {
                e.Id, e.EmployeeNumber, e.Name, e.JobTitle, e.Department,
                e.BaseSalary, e.HireDate, e.Status, e.Phone, e.Email
            })
            .FirstOrDefaultAsync();

        var perms = await _db.UserModulePermissions
            .Where(p => p.UserAccountID == id)
            .Select(p => new { p.ModuleKey, p.CanView, p.CanEdit })
            .ToListAsync();

        return Ok(new {
            id          = user.Id,
            fullName    = user.FullName,
            email       = user.Email,
            phone       = user.PhoneNumber,
            isActive    = user.IsActive,
            createdAt   = user.CreatedAt,
            roles       = roles.Where(r => r != AppRoles.Customer).ToList(),
            primaryRole = GetPrimaryRole(roles),
            employee,
            permissions = perms,
        });
    }

    // ══════════════════════════════════════════════════
    // POST /api/staff/backfill-permissions
    // زرع الصلاحيات الافتراضية للمستخدمين القدامى
    // ══════════════════════════════════════════════════
    [HttpPost("backfill-permissions")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> BackfillPermissions()
    {
        await _permService.BackfillMissingPermissionsAsync(_users);
        return Ok(new { message = "تم زرع الصلاحيات الافتراضية للمستخدمين." });
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
