using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;

    public AuthController(IAuthService auth, AppDbContext db, UserManager<AppUser> userManager)
    {
        _auth = auth;
        _db = db;
        _userManager = userManager;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        try { return Ok(await _auth.RegisterAsync(dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        try { return Ok(await _auth.LoginAsync(dto)); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _auth.ChangePasswordAsync(userId, dto);
        return result ? Ok(new { message = "Password changed successfully" }) : BadRequest(new { message = "Failed to change password" });
    }

    /// <summary>يرجع customerId للمستخدم المسجل حالياً</summary>
    [Authorize]
    [HttpGet("customer-id")]
    public async Task<IActionResult> GetCustomerId()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var customer = await _db.Customers
            .Where(c => c.AppUserId == userId && !c.IsDeleted)
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync();

        if (customer == null)
            return NotFound(new { message = "Customer profile not found" });

        return Ok(new { customerId = customer.Id });
    }

    /// <summary>بيانات المستخدم الحالي</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var userId   = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var email    = User.FindFirstValue(ClaimTypes.Email)!;
        var roles    = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        var fullName = $"{User.FindFirstValue(ClaimTypes.GivenName)} {User.FindFirstValue(ClaimTypes.Surname)}".Trim();

        var customer = await _db.Customers
            .Where(c => c.AppUserId == userId && !c.IsDeleted)
            .Select(c => new { c.Id, c.Phone })
            .FirstOrDefaultAsync();

        return Ok(new {
            userId,
            email,
            fullName,
            roles,
            customerId = customer?.Id,
            phone      = customer?.Phone
        });
    }

    /// <summary>الحصول على جميع الموظفين (للمدير ونقطة البيع)</summary>
    [Authorize(Roles = "Admin,Staff,Cashier")]
    [HttpGet("staff")]
    public async Task<IActionResult> GetStaff()
    {
        var users = await _db.Users
            .Where(u => u.IsActive)
            .Select(u => new {
                u.Id,
                u.FirstName,
                u.LastName,
                u.Email,
                u.PhoneNumber,
                FullName = $"{u.FirstName} {u.LastName}",
                Role = _db.UserRoles.Where(ur => ur.UserId == u.Id)
                        .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                        .FirstOrDefault()
            })
            .ToListAsync();

        // In a real scenario, you'd filter by role, but here we can just return all users who are not mapped to regular Customers
        // Or we can return all if this is a small POS system. Let's return all active users for simplicity, or we can use RoleManager.
        // For efficiency, we just return the users list. 
        return Ok(users);
    }

    /// <summary>تسجيل موظف جديد (للمدير)</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("staff")]
    public async Task<IActionResult> CreateStaff([FromBody] RegisterDto dto, [FromQuery] string role = "Cashier")
    {
        try {
            var existingUser = await _userManager.FindByEmailAsync(dto.Email);
            if (existingUser != null)
            {
                // Re-activate and update info if exists
                existingUser.FirstName = dto.FirstName;
                existingUser.LastName = dto.LastName;
                existingUser.IsActive = true;
                existingUser.PhoneNumber = dto.Phone;
                
                await _userManager.UpdateAsync(existingUser);
                
                // Reset password if provided (optional but good for re-activation)
                var token = await _userManager.GeneratePasswordResetTokenAsync(existingUser);
                await _userManager.ResetPasswordAsync(existingUser, token, dto.Password);

                await _auth.AssignRoleAsync(existingUser.Id, role);

                return Ok(new { message = "Staff reactivated and updated successfully" });
            }

            var authResult = await _auth.RegisterAsync(dto);
            // Delete the automatically created customer record for this staff
            var staffCustomer = await _db.Customers.FirstOrDefaultAsync(c => c.Email == dto.Email);
            if (staffCustomer != null)
            {
                staffCustomer.IsDeleted = true;
                await _db.SaveChangesAsync();
            }

            // Assign role
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user != null)
                await _auth.AssignRoleAsync(user.Id, role);

            return Ok(new { message = "Staff created successfully" }); 
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }
    /// <summary>تعديل صلاحيات الموظف (للمدير)</summary>
    [Authorize(Roles = "Admin")]
    [HttpPut("staff/{id}/role")]
    public async Task<IActionResult> UpdateStaffRole(string id, [FromQuery] string role)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null || !user.IsActive) return NotFound(new { message = "Staff not found" });

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);
        await _userManager.AddToRoleAsync(user, role);

        return Ok(new { message = "Role updated successfully" });
    }

    /// <summary>حذف الموظف (للمدير)</summary>
    [Authorize(Roles = "Admin")]
    [HttpDelete("staff/{id}")]
    public async Task<IActionResult> DeleteStaff(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null || !user.IsActive) return NotFound(new { message = "Staff not found" });

        // Soft delete the user
        user.IsActive = false;
        await _userManager.UpdateAsync(user);

        return Ok(new { message = "Staff disabled successfully" });
    }
}
