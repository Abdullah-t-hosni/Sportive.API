using Sportive.API.Attributes;
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

    /// <summary>ØªØ¬Ø¯ÙŠØ¯ Ø§Ù„Ù€ access token â€” Ø§Ù„ÙØ±ÙˆÙ†Øª ÙŠØ¨Ø¹Øª refreshToken ÙŠØ¬ÙŠØ¨ access token Ø¬Ø¯ÙŠØ¯</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.RefreshToken))
            return BadRequest(new { message = "refreshToken Ù…Ø·Ù„ÙˆØ¨" });
        try { return Ok(await _auth.RefreshTokenAsync(dto.RefreshToken)); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
    }

    /// <summary>ØªØ³Ø¬ÙŠÙ„ Ø®Ø±ÙˆØ¬ â€” ÙŠÙÙ„ØºÙŠ Ø§Ù„Ù€ refresh token</summary>
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        await _auth.RevokeRefreshTokenAsync(userId);
        return Ok(new { message = "ØªÙ… ØªØ³Ø¬ÙŠÙ„ Ø§Ù„Ø®Ø±ÙˆØ¬ Ø¨Ù†Ø¬Ø§Ø­" });
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Identifier ?? "") 
                   ?? await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Identifier);
        
        if (user == null)
            return NotFound(new { message = "User not found with this identifier" });

        // âœ… FIX: Ø§Ø³ØªØ®Ø¯Ø§Ù… RandomNumberGenerator Ø§Ù„Ø¢Ù…Ù† Ø¨Ø¯Ù„Ø§Ù‹ Ù…Ù† new Random() ØºÙŠØ± Ø§Ù„Ø¢Ù…Ù†
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        _cache.Set($"ResetCode_{dto.Identifier}", code, TimeSpan.FromMinutes(10));

        // ðŸ›¡ï¸ SECURITY FIX: Only send via email/WhatsApp, never return in production!
        if (!string.IsNullOrEmpty(user.Email))
        {
            var subject = "ÙƒÙˆØ¯ Ø¥Ø¹Ø§Ø¯Ø© ØªØ¹ÙŠÙŠÙ† ÙƒÙ„Ù…Ø© Ø§Ù„Ø³Ø± - Sportive";
            var body = $@"
                <div dir='rtl' style='font-family: Arial, sans-serif; border: 1px solid #eee; padding: 20px;'>
                    <h2 style='color: #0f3460;'>Sportive Store</h2>
                    <p>Ø£Ù‡Ù„Ø§Ù‹ Ø¨Ùƒ {user.FullName}ØŒ</p>
                    <p>ÙƒÙˆØ¯ Ø§Ø³ØªØ¹Ø§Ø¯Ø© ÙƒÙ„Ù…Ø© Ø§Ù„Ø³Ø± Ø§Ù„Ø®Ø§Øµ Ø¨Ùƒ Ù‡Ùˆ:</p>
                    <div style='background: #f4f4f4; padding: 15px; font-size: 24px; font-weight: bold; text-align: center; border-radius: 5px;'>{code}</div>
                    <p>Ù‡Ø°Ø§ Ø§Ù„ÙƒÙˆØ¯ ØµØ§Ù„Ø­ Ù„Ù…Ø¯Ø© 10 Ø¯Ù‚Ø§Ø¦Ù‚ ÙÙ‚Ø·.</p>
                    <p>Ø¥Ø°Ø§ Ù„Ù… ØªÙƒÙ† Ø£Ù†Øª Ù…Ù† Ø·Ù„Ø¨ Ù‡Ø°Ø§ Ø§Ù„ÙƒÙˆØ¯ØŒ ÙŠØ±Ø¬Ù‰ ØªØ¬Ø§Ù‡Ù„ Ù‡Ø°Ù‡ Ø§Ù„Ø±Ø³Ø§Ù„Ø©.</p>
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
        // âœ… FIX: Ø§Ø³ØªØ®Ø¯Ø§Ù… RandomNumberGenerator Ø§Ù„Ø¢Ù…Ù†
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        // Ø­ÙØ¸ Ø§Ù„ÙƒÙˆØ¯ Ù„Ù…Ø¯Ø© 5 Ø¯Ù‚Ø§Ø¦Ù‚
        _cache.Set($"OtpCode_{dto.PhoneNumber}", code, TimeSpan.FromMinutes(5));

        bool isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        
        // Ø¥Ø±Ø³Ø§Ù„ Ø±Ø³Ø§Ù„Ø© Ø¨Ø§Ø³ØªØ®Ø¯Ø§Ù… Ø§Ù„ÙˆØ§ØªØ³Ø§Ø¨
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

        // ÙÙŠ Ø­Ø§Ù„ Ø§Ù„Ù†Ø¬Ø§Ø­ Ù†Ù‚ÙˆÙ… Ø¨Ù…Ø³Ø­ Ø§Ù„ÙƒÙˆØ¯ Ù…Ù† Ø§Ù„Ù€ Cache
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

    /// <summary>ÙŠØ±Ø¬Ø¹ customerId Ù„Ù„Ù…Ø³ØªØ®Ø¯Ù… Ø§Ù„Ù…Ø³Ø¬Ù„ Ø­Ø§Ù„ÙŠØ§Ù‹</summary>
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

    /// <summary>Ø¨ÙŠØ§Ù†Ø§Øª Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù… Ø§Ù„Ø­Ø§Ù„ÙŠ</summary>
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

    [RequirePermission(ModuleKeys.Staff)]
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

    /// <summary>ØªØ³Ø¬ÙŠÙ„ Ù…ÙˆØ¸Ù Ø¬Ø¯ÙŠØ¯ (Ù„Ù„Ù…Ø¯ÙŠØ±)</summary>
    [RequirePermission(ModuleKeys.Staff, requireEdit: true)]
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
    // Role management and staff deletion have been moved to StaffController for consistency.

    private static List<string> GetDefaultRolePermissions(IList<string> roles)
    {
        var perms = new HashSet<string>();
        // Use case-insensitive check
        bool Is(string role) => roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
        
        // â”€â”€ Admin & Manager: Full Access Baseline â”€â”€
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

        // â”€â”€ Admin Exclusive â”€â”€
        if (Is("Admin"))
        {
            perms.Add("staff"); perms.Add("staff.edit");
            perms.Add("settings"); perms.Add("settings.edit");
            perms.Add("backup");
            perms.Add("whatsapp");
        }

        // â”€â”€ Cashier: POS & Orders Operations â”€â”€
        if (Is("Cashier"))
        {
            perms.Add("pos");
            perms.Add("pos.returns");
            perms.Add("orders");
            perms.Add("orders.read");
            perms.Add("customers"); // To add/select customers during sale
        }

        // â”€â”€ Accountant: Financial & Reporting â”€â”€
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

        // â”€â”€ Staff (Store Keeper / Sales): Inventory & Orders â”€â”€
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

