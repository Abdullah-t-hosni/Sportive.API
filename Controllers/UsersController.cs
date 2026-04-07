using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class UsersController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly AppDbContext _db;

    public UsersController(UserManager<AppUser> userManager, RoleManager<IdentityRole> roleManager, AppDbContext db)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
    }

    // ── Get All Users (with Roles) ───────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAllUsers([FromQuery] string? search = null, [FromQuery] string? role = null)
    {
        var query = from u in _userManager.Users
                    join ur in _db.UserRoles on u.Id equals ur.UserId into userRoles
                    from ur in userRoles.DefaultIfEmpty()
                    join r in _db.Roles on ur.RoleId equals r.Id into roles
                    from r in roles.DefaultIfEmpty()
                    select new { u, Role = r.Name };

        var list = await query.ToListAsync();

        // Group by user id (as users can have multiple roles theoretically)
        var grouped = list.GroupBy(x => x.u.Id)
                          .Select(g => new {
                              id          = g.Key,
                              fullName    = g.First().u.FullName,
                              email       = g.First().u.Email,
                              phone       = g.First().u.PhoneNumber,
                              isActive    = g.First().u.IsActive,
                              createdAt   = g.First().u.CreatedAt,
                              roles       = g.Where(x => x.Role != null).Select(x => x.Role).ToList()
                          });

        if (!string.IsNullOrEmpty(search))
        {
            grouped = grouped.Where(u => u.fullName.Contains(search, StringComparison.OrdinalIgnoreCase)
                                     || (u.email != null && u.email.Contains(search, StringComparison.OrdinalIgnoreCase))
                                     || (u.phone != null && u.phone.Contains(search)));
        }

        if (!string.IsNullOrEmpty(role))
        {
            grouped = grouped.Where(u => u.roles.Contains(role));
        }

        return Ok(grouped.OrderByDescending(u => u.createdAt));
    }

    // ── Update User Basic Info ──────────────────────────────
    public record UpdateUserDto(string FullName, string Email, string? Phone, bool? IsActive);
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateUserDto dto)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound(new { message = "المستخدم غير موجود" });

        user.FullName    = dto.FullName;
        user.Email       = dto.Email;
        user.UserName    = dto.Email; // Keep UserName and Email same for simplicity
        user.PhoneNumber = dto.Phone;
        if (dto.IsActive.HasValue) user.IsActive = dto.IsActive.Value;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded) return BadRequest(result.Errors);

        return Ok(new { message = "تم تحديث بيانات المستخدم بنجاح" });
    }

    // ── Reset Password (ADMIN OVERRIDE) ────────────────────
    public record ResetPasswordDto(string NewPassword);
    [HttpPost("{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] ResetPasswordDto dto)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound(new { message = "المستخدم غير موجود" });

        if (string.IsNullOrWhiteSpace(dto.NewPassword)) return BadRequest(new { message = "كلمة المرور لا يمكن أن تكون فارغة" });

        // 🛡️ STRATEGY: Hard override by removing and adding password
        await _userManager.RemovePasswordAsync(user);
        var result = await _userManager.AddPasswordAsync(user, dto.NewPassword);

        if (!result.Succeeded) 
            return BadRequest(new { message = "فشل تغيير كلمة المرور", errors = result.Errors.Select(e => e.Description) });

        return Ok(new { message = "تم تغيير كلمة المرور بنجاح" });
    }

    // ── Update User Roles ───────────────────────────────────
    public record UpdateRolesDto(List<string> Roles);
    [HttpPut("{id}/roles")]
    public async Task<IActionResult> UpdateRoles(string id, [FromBody] UpdateRolesDto dto)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound(new { message = "المستخدم غير موجود" });

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);
        
        // Ensure roles exist
        foreach(var roleName in dto.Roles)
        {
            if (!await _roleManager.RoleExistsAsync(roleName))
                await _roleManager.CreateAsync(new IdentityRole(roleName));
        }

        var result = await _userManager.AddToRolesAsync(user, dto.Roles);
        if (!result.Succeeded) return BadRequest(result.Errors);

        return Ok(new { message = "تم تحديث الصلاحيات بنجاح" });
    }

    // ── Delete User ───────────────────────────────────────── (Soft delete/Deactivate or Hard Delete)
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(string id, [FromQuery] bool permanent = false)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound(new { message = "المستخدم غير موجود / User not found" });

        if (permanent)
        {
            // 1. Check for Customer profile
            var customer = await _db.Customers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.AppUserId == id);

            if (customer != null)
            {
                // 2. Check for Orders (Foreign Key Constraint)
                var hasOrders = await _db.Orders.IgnoreQueryFilters()
                    .AnyAsync(o => o.CustomerId == customer.Id || o.SalesPersonId == id);

                if (hasOrders)
                {
                    return BadRequest(new { 
                        message = "لا يمكن حذف هذا المستخدم نهائياً لوجود طلبات مرتبطة به. برجاء تعطيل الحساب بدلاً من الحذف الزائد.",
                        details = "This user has order history and cannot be permanently deleted due to database integrity. Use 'Deactivate' (Soft Delete) instead." 
                    });
                }

                // 3. Delete associated records that might not be cascaded or cause issues
                _db.Customers.Remove(customer);
                await _db.SaveChangesAsync();
            }

            // 4. Delete the Identity User
            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded) return BadRequest(result.Errors);
        }
        else
        {
            user.IsActive = false;
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded) return BadRequest(result.Errors);
        }

        return Ok(new { message = "تم حذف المستخدم بنجاح / Operation successful" });
    }
}
