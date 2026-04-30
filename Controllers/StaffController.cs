using Sportive.API.Attributes;
using Sportive.API.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Interfaces;
using System.Security.Claims;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.Staff, requireEdit: true)]
public class StaffController : ControllerBase
{
    private readonly UserManager<AppUser>      _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly AppDbContext              _db;
    private readonly StaffPermissionService    _permService;
    private readonly SequenceService           _sequence;
    private readonly ICustomerService          _customerService;
    private readonly ICacheService             _cache;
    private readonly IAuditService             _audit;

    private readonly ITranslator             _t;

    public StaffController(
        UserManager<AppUser>      users,
        RoleManager<IdentityRole> roles,
        AppDbContext              db,
        StaffPermissionService    permService,
        SequenceService           sequence,
        ICustomerService          customerService,
        ICacheService             cache,
        IAuditService             audit,
        ITranslator               translator)
    {
        _users           = users;
        _roles           = roles;
        _db              = db;
        _permService     = permService;
        _sequence        = sequence;
        _customerService = customerService;
        _cache           = cache;
        _audit           = audit;
        _t               = translator;
    }

    // GET /api/staff
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var staffRoles = AppRoles.StaffRoles.ToList();

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
                role           = GetPrimaryRole(userRoles),
                createdAt      = user.CreatedAt,
                employeeId     = link?.Id,
                employeeNumber = link?.EmployeeNumber,
            });
        }

        return Ok(staffUsers.OrderBy(s => ((dynamic)s).role));
    }

    // POST /api/staff
    // إنشاء موظف جديد
    // --------------------------------------------------
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStaffDto dto)
    {
        // تحقق من صحة الدور
        var validRole = AppRoles.StaffRoles.FirstOrDefault(r => r.Equals(dto.Role, StringComparison.OrdinalIgnoreCase));
        if (validRole == null)
            return BadRequest(new { message = _t.Get("Staff.InvalidRole", dto.Role) });
        dto = dto with { Role = validRole };

        // تحقق من التكرار
        if (await _users.Users.AnyAsync(u => u.Email == dto.Email && u.UserName!.StartsWith("staff_")))
            return BadRequest(new { message = _t.Get("Auth.EmailInUse") });

        var phoneExists = await _users.Users.AnyAsync(u => u.PhoneNumber == dto.Phone && u.UserName!.StartsWith("staff_"));
        if (phoneExists)
            return BadRequest(new { message = _t.Get("Auth.PhoneInUse") });

        // إنشاء الأدوار لو مش موجودة
        await EnsureRolesAsync();

        var user = new AppUser
        {
            UserName    = "staff_" + (!string.IsNullOrEmpty(dto.Phone) ? dto.Phone : dto.Email),
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

        // ✅ ربط أو إنشاء سجل HR وتحضير الحسابات المحاسبية (اختياري)
        if (dto.IsEmployee)
        {
            await EnsureEmployeeLinkAsync(user, dto.Role);
        }

        return Ok(new {
            id       = user.Id,
            fullName = user.FullName,
            role     = dto.Role,
            message  = _t.Get("Staff.CreatedWithHR", GetRoleAr(dto.Role))
        });
    }

    // PUT /api/staff/{id}/role
    // تغيير دور موظف
    [HttpPut("{id}/role")]
    public async Task<IActionResult> ChangeRole(string id, [FromBody] ChangeRoleDto dto)
    {
        var validRole = AppRoles.StaffRoles.FirstOrDefault(r => r.Equals(dto.Role, StringComparison.OrdinalIgnoreCase));
        if (validRole == null)
            return BadRequest(new { message = _t.Get("Staff.InvalidRole", dto.Role) });
        dto = dto with { Role = validRole };

        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        // منع تغيير دور الأدمن الأصلي
        if (user.Email == "admin@sportive.com" && dto.Role != AppRoles.Admin)
            return BadRequest(new { message = _t.Get("Staff.CannotChangeRootAdmin") });

        var currentRoles = await _users.GetRolesAsync(user);
        var staffRoles   = currentRoles.Where(r => r != AppRoles.Customer).ToList();

        // احذف الأدوار القديمة وأضف الجديد
        if (staffRoles.Any())
            await _users.RemoveFromRolesAsync(user, staffRoles);
        
        var result = await _users.AddToRoleAsync(user, dto.Role);
        if (!result.Succeeded)
            return BadRequest(new { message = _t.Get("Staff.RoleChangeFailed", result.Errors.FirstOrDefault()?.Description ?? "") });

        // إعادة زرع الصلاحيات الافتراضية للدور الجديد
        await _permService.SeedDefaultPermissionsAsync(user.Id, dto.Role);
        await _cache.RemoveAsync($"UserPermissions_{user.Id}");

        // ✅ تحديث سجل HR المرتبط إن وجد
        var hasEmployee = await _db.Employees.AnyAsync(e => e.AppUserId == user.Id);
        if (hasEmployee)
        {
            await EnsureEmployeeLinkAsync(user, dto.Role);
        }

        await _audit.LogAsync("ChangeRole", "User", user.Id, $"Role changed to {dto.Role}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name));

        return Ok(new {
            id,
            newRole  = dto.Role,
            roleAr   = GetRoleAr(dto.Role),
            message  = _t.Get("Staff.RoleChangeSuccess")
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

    // POST /api/staff/{id}/link-employee
    // ربط مستخدم حالي بسجل موظف (يدوياً)
    [HttpPost("{id}/link-employee")]
    public async Task<IActionResult> LinkEmployee(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        var roles = await _users.GetRolesAsync(user);
        var role = GetPrimaryRole(roles);

        await EnsureEmployeeLinkAsync(user, role);
        return Ok(new { message = _t.Get("Staff.LinkSuccess") });
    }

    // PATCH /api/staff/{id}/toggle-active
    // تفعيل / تعطيل الموظف
    [HttpPatch("{id}/toggle-active")]
    public async Task<IActionResult> ToggleActive(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();
        if (user.Email == "admin@sportive.com")
            return BadRequest(new { message = _t.Get("Staff.CannotDeactivateRootAdmin") });

        user.IsActive = !user.IsActive;
        await _users.UpdateAsync(user);

        return Ok(new { isActive = user.IsActive });
    }

    // PUT /api/staff/{id}/reset-password
    // إعادة تعيين كلمة مرور موظف
    [HttpPut("{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] StaffResetPasswordDto dto)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        var token  = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, token, dto.NewPassword);

        return result.Succeeded
            ? Ok(new { message = _t.Get("Users.PasswordChangeSuccess") })
            : BadRequest(new { errors = result.Errors.Select(e => e.Description) });
    }

    // DELETE /api/staff/{id}
    // حذف موظف نهائياً أو تعطيله
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();
        if (user.Email == "admin@sportive.com")
            return BadRequest(new { message = _t.Get("Staff.CannotDeleteRootAdmin") });

        // نفضل التعطيل (Soft Delete) لو كان هناك سجلات مرتبطة به
        var hasOrders = await _db.Orders.AnyAsync(o => o.SalesPersonId == id);
        if (hasOrders)
        {
            user.IsActive = false;
            await _users.UpdateAsync(user);
            return Ok(new { message = _t.Get("Staff.DeactivatedDueToOrders") });
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
            ? Ok(new { message = _t.Get("Staff.DeleteSuccess") })
            : BadRequest(new { errors = result.Errors.Select(e => e.Description) });
    }

    // GET /api/staff/{id}/permissions
    // جلب صلاحيات المستخدم الخاصة
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

    // PUT /api/staff/{id}/permissions
    // تحديث صلاحيات المستخدم (الحق فى الرؤية أو التحكم)
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
        await _cache.RemoveAsync($"UserPermissions_{id}");
        
        await _audit.LogAsync("UpdatePermissions", "UserModulePermission", id, "Permissions updated manually", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name));

        return Ok(new { message = _t.Get("Users.PermissionsUpdateSuccess") });
    }

    // ==================================================================================================
    // GET /api/staff/{id}/profile
    // بروفايل كامل: بيانات المستخدم + HR + الصلاحيات
    // ==================================================================================================
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

    // POST /api/staff/backfill-permissions

    [HttpPost("backfill-permissions")]
    [RequirePermission(ModuleKeys.Staff, requireEdit: true)]
    public async Task<IActionResult> BackfillPermissions()
    {
        await _permService.BackfillMissingPermissionsAsync(_users);
        return Ok(new { message = _t.Get("Staff.BackfillSuccess") });
    }

    // GET /api/staff/roles

    [HttpGet("roles")]
    public IActionResult GetRoles()
    {
        return Ok(new[]
        {
            new { value = AppRoles.Admin,      labelAr = _t.Get("Roles.Admin"),      labelEn = "System Admin",  permissions = _t.Get("Roles.Admin.Perms") },
            new { value = AppRoles.Manager,    labelAr = _t.Get("Roles.Manager"),    labelEn = "Branch Manager", permissions = _t.Get("Roles.Manager.Perms") },
            new { value = AppRoles.Cashier,    labelAr = _t.Get("Roles.Cashier"),    labelEn = "Cashier",        permissions = _t.Get("Roles.Cashier.Perms") },
            new { value = AppRoles.Accountant, labelAr = _t.Get("Roles.Accountant"), labelEn = "Accountant",     permissions = _t.Get("Roles.Accountant.Perms") },
            new { value = AppRoles.Staff,      labelAr = _t.Get("Roles.Staff"),      labelEn = "Staff",          permissions = _t.Get("Roles.Staff.Perms") },
        });
    }

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

    private string GetRoleAr(string role) => role switch
    {
        AppRoles.Admin      => _t.Get("Roles.Admin"),
        AppRoles.Manager    => _t.Get("Roles.Manager"),
        AppRoles.Cashier    => _t.Get("Roles.Cashier"),
        AppRoles.Accountant => _t.Get("Roles.Accountant"),
        AppRoles.Staff      => _t.Get("Roles.Staff"),
        _                   => role
    };
}

public record CreateStaffDto(
    string FullName,
    string Email,
    string Phone,
    string Password,
    string Role,
    bool IsEmployee = true
);

public record ChangeRoleDto(string Role);
public record StaffResetPasswordDto(string NewPassword);
public record UserModulePermissionDto(string ModuleKey, bool CanView, bool CanEdit);

