using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly IMemoryCache _cache;
    private readonly IEmailService _email;
    private readonly IWhatsAppApiService _whatsappApi;

    public AuthController(IAuthService auth, AppDbContext db, UserManager<AppUser> userManager, IMemoryCache cache, IEmailService email, IWhatsAppApiService whatsappApi)
    {
        _auth = auth;
        _db = db;
        _userManager = userManager;
        _cache = cache;
        _email = email;
        _whatsappApi = whatsappApi;
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

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Identifier ?? "") 
                   ?? await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Identifier);
        
        if (user == null)
            return NotFound(new { message = "User not found with this identifier" });

        // GENERATE MOCK CODE (100000 - 999999)
        var code = new Random().Next(100000, 999999).ToString();
        _cache.Set($"ResetCode_{dto.Identifier}", code, TimeSpan.FromMinutes(10));

        // 🛡️ SECURITY FIX: Only send via email/WhatsApp, never return in production!
        if (!string.IsNullOrEmpty(user.Email))
        {
            var subject = "كود إعادة تعيين كلمة السر - Sportive";
            var body = $@"
                <div dir='rtl' style='font-family: Arial, sans-serif; border: 1px solid #eee; padding: 20px;'>
                    <h2 style='color: #0f3460;'>Sportive Store</h2>
                    <p>أهلاً بك {user.FirstName}،</p>
                    <p>كود استعادة كلمة السر الخاص بك هو:</p>
                    <div style='background: #f4f4f4; padding: 15px; font-size: 24px; font-weight: bold; text-align: center; border-radius: 5px;'>{code}</div>
                    <p>هذا الكود صالح لمدة 10 دقائق فقط.</p>
                    <p>إذا لم تكن أنت من طلب هذا الكود، يرجى تجاهل هذه الرسالة.</p>
                </div>";
            
            await _email.SendEmailAsync(user.Email, subject, body);
        }

        bool isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        
        return Ok(new { 
            message = "Authentication code sent to your registered email/phone.",
            code = isDev ? code : null, 
            supportPhone = "201021461937"
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        if (!_cache.TryGetValue($"ResetCode_{dto.Identifier}", out string? cachedCode) || cachedCode != dto.Code)
        {
            return BadRequest(new { message = "Invalid or expired verify code" });
        }

        var user = await _userManager.FindByEmailAsync(dto.Identifier ?? "") 
                   ?? await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Identifier);

        if (user == null) return NotFound(new { message = "User no longer exists" });

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);

        if (!result.Succeeded)
            return BadRequest(new { message = string.Join(", ", result.Errors.Select(e => e.Description)) });

        _cache.Remove($"ResetCode_{dto.Identifier}");
        return Ok(new { message = "Password reset successful" });
    }

    [HttpPost("send-otp")]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpDto dto)
    {
        var code = new Random().Next(100000, 999999).ToString();
        // حفظ الكود لمدة 5 دقائق
        _cache.Set($"OtpCode_{dto.PhoneNumber}", code, TimeSpan.FromMinutes(5));

        bool isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        
        // إرسال رسالة باستخدام الواتساب
        bool sent = await _whatsappApi.SendOtpAsync(dto.PhoneNumber, code);

        if (!sent && !isDev)
        {
            return BadRequest(new { message = "Failed to send OTP message via WhatsApp" });
        }

        return Ok(new { 
            message = "OTP sent successfully to your WhatsApp.",
            code = isDev ? code : null 
        });
    }

    [HttpPost("verify-otp")]
    public IActionResult VerifyOtp([FromBody] VerifyOtpDto dto)
    {
        if (!_cache.TryGetValue($"OtpCode_{dto.PhoneNumber}", out string? cachedCode) || cachedCode != dto.Code)
        {
            return BadRequest(new { message = "Invalid or expired OTP code" });
        }

        // في حال النجاح نقوم بمسح الكود من الـ Cache
        _cache.Remove($"OtpCode_{dto.PhoneNumber}");
        return Ok(new { message = "OTP verified successfully" });
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        var result = await _auth.ChangePasswordAsync(userId, dto);
        return result ? Ok(new { message = "Password changed successfully" }) : BadRequest(new { message = "Failed to change password" });
    }

    /// <summary>يرجع customerId للمستخدم المسجل حالياً</summary>
    [Authorize]
    [HttpGet("customer-id")]
    public async Task<IActionResult> GetCustomerId()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
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
        var userId   = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        var email    = User.FindFirst(ClaimTypes.Email)?.Value!;
        var roles    = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        var fullName    = $"{User.FindFirst(ClaimTypes.GivenName)?.Value} {User.FindFirst(ClaimTypes.Surname)?.Value}".Trim();
        
        // 1. Get static role-based baseline
        var permissions = GetDefaultRolePermissions(roles);

        // 2. Override with DB-specific ones (The Checkboxes system)
        var overrides = await _db.UserModulePermissions.Where(p => p.UserAccountID == userId).ToListAsync();
        foreach (var over in overrides)
        {
            if (over.CanView)
            {
                if (!permissions.Contains(over.ModuleKey)) permissions.Add(over.ModuleKey);
                if (over.CanEdit && !permissions.Contains($"{over.ModuleKey}.edit")) permissions.Add($"{over.ModuleKey}.edit");
            }
            else
            {
                permissions.Remove(over.ModuleKey);
                permissions.Remove($"{over.ModuleKey}.edit");
            }
        }

        var customer = await _db.Customers
            .Where(c => c.AppUserId == userId && !c.IsDeleted)
            .Select(c => new { c.Id, c.Phone })
            .FirstOrDefaultAsync();

        return Ok(new {
            userId,
            email,
            fullName,
            roles,
            permissions,
            customerId = customer?.Id,
            phone      = customer?.Phone
        });
    }

    [Authorize(Roles = "Admin,Staff,Cashier")]
    [HttpGet("staff")]
    public async Task<IActionResult> GetStaff()
    {
        var staffRoles = new[] { "Admin", "Staff", "Cashier" };
        
        var users = await (from u in _db.Users
                          join ur in _db.UserRoles on u.Id equals ur.UserId
                          join r in _db.Roles on ur.RoleId equals r.Id
                          where u.IsActive && staffRoles.Contains(r.Name)
                          select new {
                              u.Id,
                              u.FirstName,
                              u.LastName,
                              u.Email,
                              u.PhoneNumber,
                              FullName = u.FirstName + " " + u.LastName,
                              Role = r.Name
                          }).ToListAsync();

        return Ok(users);
    }

    /// <summary>تسجيل موظف جديد (للمدير)</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("staff")]
    public async Task<IActionResult> CreateStaff([FromBody] RegisterDto dto, [FromQuery] string role = "Cashier")
    {
        try {
            var existingUser = await _userManager.FindByEmailAsync(dto.Email ?? "");
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

            var authResult = await _auth.RegisterAsync(dto, isCustomer: false);

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

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded) 
            return BadRequest(new { message = "Failed to delete staff: " + result.Errors.FirstOrDefault()?.Description });

        return Ok(new { message = "Staff deleted permanently" });
    }

    private static List<string> GetDefaultRolePermissions(IList<string> roles)
    {
        var perms = new HashSet<string>();
        if (roles.Contains("Admin") || roles.Contains("Manager"))
        {
            perms.Add("dashboard"); perms.Add("orders"); perms.Add("products");
            perms.Add("customers"); perms.Add("categories"); perms.Add("coupons");
            perms.Add("reports"); perms.Add("purchases"); perms.Add("accounting");
            perms.Add("pos"); perms.Add("backup"); perms.Add("whatsapp");
        }
        if (roles.Contains("Admin"))
        {
            perms.Add("staff"); perms.Add("settings"); perms.Add("backup");
        }
        if (roles.Contains("Cashier"))
        {
            perms.Add("pos"); perms.Add("orders.read");
        }
        if (roles.Contains("Accountant"))
        {
            perms.Add("reports"); perms.Add("accounting"); perms.Add("purchases");
            perms.Add("orders.read");
        }
        if (roles.Contains("Staff"))
        {
            perms.Add("orders"); perms.Add("products.read"); perms.Add("customers.read");
        }
        return perms.ToList();
    }
}
