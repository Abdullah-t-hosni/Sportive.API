using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Microsoft.Extensions.Caching.Memory;
using Sportive.API.Services;
using Sportive.API.Utils;

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
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        try { return Ok(await _auth.RegisterAsync(dto)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        try { return Ok(await _auth.LoginAsync(dto)); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
    }

    /// <summary>تجديد الـ access token — الفرونت يبعت refreshToken يجيب access token جديد</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.RefreshToken))
            return BadRequest(new { message = "refreshToken مطلوب" });
        try { return Ok(await _auth.RefreshTokenAsync(dto.RefreshToken)); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
    }

    /// <summary>تسجيل خروج — يُلغي الـ refresh token</summary>
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        await _auth.RevokeRefreshTokenAsync(userId);
        return Ok(new { message = "تم تسجيل الخروج بنجاح" });
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Identifier ?? "") 
                   ?? await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Identifier);
        
        if (user == null)
            return NotFound(new { message = "User not found with this identifier" });

        // ✅ FIX: استخدام RandomNumberGenerator الآمن بدلاً من new Random() غير الآمن
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        _cache.Set($"ResetCode_{dto.Identifier}", code, TimeSpan.FromMinutes(10));

        // 🛡️ SECURITY FIX: Only send via email/WhatsApp, never return in production!
        if (!string.IsNullOrEmpty(user.Email))
        {
            var subject = "كود إعادة تعيين كلمة السر - Sportive";
            var body = $@"
                <div dir='rtl' style='font-family: Arial, sans-serif; border: 1px solid #eee; padding: 20px;'>
                    <h2 style='color: #0f3460;'>Sportive Store</h2>
                    <p>أهلاً بك {user.FullName}،</p>
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
    [EnableRateLimiting("auth")]
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
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpDto dto)
    {
        // ✅ FIX: استخدام RandomNumberGenerator الآمن
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
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
            message = "OTP sent successfully to your WhatsApp."
        });
    }

    [HttpPost("verify-otp")]
    [EnableRateLimiting("auth")]
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
            .Where(c => c.AppUserId == userId)
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
        var fullName    = User.FindFirst(ClaimTypes.Name)?.Value ?? "";
        
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
            .Where(c => c.AppUserId == userId)
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
        var staffRoles = new[] { "Admin", "Staff", "Cashier", "Manager", "Accountant" };
        
        var users = await (from u in _db.Users
                          join ur in _db.UserRoles on u.Id equals ur.UserId
                          join r in _db.Roles on ur.RoleId equals r.Id
                          join e in _db.Employees on u.Id equals e.AppUserId into empJoin
                          from e in empJoin.DefaultIfEmpty()
                          where u.IsActive && staffRoles.Contains(r.Name)
                          select new {
                              u.Id,
                              u.FullName,
                              u.Email,
                              u.PhoneNumber,
                              Role = r.Name,
                              EmployeeNumber = e != null ? e.EmployeeNumber : null
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
                existingUser.FullName = dto.FullName;
                existingUser.IsActive = true;
                existingUser.PhoneNumber = dto.Phone;
                
                await _userManager.UpdateAsync(existingUser);
                
                // Reset password if provided (optional but good for re-activation)
                if (!string.IsNullOrEmpty(dto.Password))
                {
                    var token = await _userManager.GeneratePasswordResetTokenAsync(existingUser);
                    await _userManager.ResetPasswordAsync(existingUser, token, dto.Password);
                }

                await _auth.AssignRoleAsync(existingUser.Id, role);
                await EnsureEmployeeLinkAsync(existingUser, role);

                return Ok(new { message = "Staff reactivated and updated successfully" });
            }

            var authResult = await _auth.RegisterAsync(dto, isCustomer: false);

            // Assign role
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email || u.PhoneNumber == dto.Phone);
            if (user != null)
            {
                await _auth.AssignRoleAsync(user.Id, role);
                await EnsureEmployeeLinkAsync(user, role);
            }

            return Ok(new { message = "Staff created successfully" }); 
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    private async Task EnsureEmployeeLinkAsync(AppUser user, string role)
    {
        // 1. Check if employee record already exists for this user
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.AppUserId == user.Id);
        if (employee == null)
        {
            // 2. Try to find by phone if not linked by ID
            employee = await _db.Employees.FirstOrDefaultAsync(e => e.Phone == user.PhoneNumber && e.AppUserId == null);
            if (employee != null)
            {
                employee.AppUserId = user.Id;
            }
            else
            {
                // 3. Create new HR profile
                var sequence = HttpContext.RequestServices.GetRequiredService<SequenceService>();
                var empNo = await sequence.NextAsync("EMP", async (db, pattern) =>
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
                    JobTitle = role, // Use role as initial job title
                    HireDate = TimeHelper.GetEgyptTime(),
                    Status = EmployeeStatus.Active,
                    CreatedAt = TimeHelper.GetEgyptTime()
                };
                _db.Employees.Add(employee);
            }
        }
        else
        {
            // Update existing record with the new user info if needed
            employee.Name = user.FullName;
            employee.Email = user.Email;
            employee.Phone = user.PhoneNumber;
            employee.Status = EmployeeStatus.Active;
        }

        await _db.SaveChangesAsync();

        // 4. Ensure Employee has a ledger account for HR operations
        var customerService = HttpContext.RequestServices.GetRequiredService<ICustomerService>();
        await customerService.EnsureCustomerAccountAsync(0, isEmployee: true, employeeId: employee.Id);
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
        // Use case-insensitive check
        bool Is(string role) => roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
        
        // ── Admin & Manager: Full Access Baseline ──
        if (Is("Admin") || Is("Manager"))
        {
            perms.Add("dashboard"); 
            perms.Add("orders"); perms.Add("orders.edit"); perms.Add("orders.delete");
            perms.Add("returns"); perms.Add("returns.edit");
            perms.Add("products"); perms.Add("products.edit");
            perms.Add("categories"); perms.Add("categories.edit");
            perms.Add("customers"); perms.Add("customers.edit");
            perms.Add("coupons"); perms.Add("coupons.edit");
            perms.Add("purchases"); perms.Add("purchases.edit");
            perms.Add("accounting"); perms.Add("accounting.edit");
            perms.Add("hr"); perms.Add("hr.edit");
            perms.Add("fixed-assets"); perms.Add("fixed-assets.edit");
            perms.Add("inventory-count"); perms.Add("inventory-count.edit");
            perms.Add("inventory-opening"); perms.Add("inventory-opening.edit");
            perms.Add("pos"); perms.Add("pos.verify"); perms.Add("pos.returns"); perms.Add("pos.discount");
            perms.Add("reports");
            perms.Add("import");
        }

        // ── Admin Exclusive ──
        if (Is("Admin"))
        {
            perms.Add("staff"); perms.Add("staff.edit");
            perms.Add("settings"); perms.Add("settings.edit");
            perms.Add("backup");
            perms.Add("whatsapp");
        }

        // ── Cashier: POS & Orders Operations ──
        if (Is("Cashier"))
        {
            perms.Add("pos");
            perms.Add("pos.returns");
            perms.Add("orders");
            perms.Add("orders.read");
            perms.Add("customers"); // To add/select customers during sale
        }

        // ── Accountant: Financial & Reporting ──
        if (Is("Accountant"))
        {
            perms.Add("dashboard");
            perms.Add("accounting"); perms.Add("accounting.edit");
            perms.Add("purchases"); perms.Add("purchases.edit");
            perms.Add("hr"); // To see payroll
            perms.Add("reports");
            perms.Add("orders.read");
            perms.Add("products.read");
        }

        // ── Staff (Store Keeper / Sales): Inventory & Orders ──
        if (Is("Staff"))
        {
            perms.Add("orders");
            perms.Add("orders.edit");
            perms.Add("products"); 
            perms.Add("products.edit");
            perms.Add("inventory-count");
            perms.Add("customers");
        }

        return perms.ToList();
    }
}
