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
using Hangfire;

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
    private readonly IAuditService _audit;
    private readonly ITranslator _translator;
    private readonly ISecurityEventsEngine _securityEngine;

    public AuthController(
        IAuthService auth, 
        AppDbContext db, 
        UserManager<AppUser> userManager, 
        IMemoryCache cache, 
        IEmailService email, 
        IWhatsAppApiService whatsappApi, 
        IAuditService audit, 
        ITranslator translator,
        ISecurityEventsEngine securityEngine)
    {
        _auth = auth;
        _db = db;
        _userManager = userManager;
        _cache = cache;
        _email = email;
        _whatsappApi = whatsappApi;
        _audit = audit;
        _translator = translator;
        _securityEngine = securityEngine;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        // 🛡️ Email Verification Enforcement
        if (string.IsNullOrEmpty(dto.Email))
            return BadRequest(new { message = _translator.Get("Auth.EmailRequired") });

        // 💡 Verification logic is temporarily commented out to save email credits / limit issues
        /*
        if (!_cache.TryGetValue($"RegisterCode_{dto.Email}", out string? cachedCode) || cachedCode != dto.Code)
        {
            return BadRequest(new { message = _translator.Get("Auth.InvalidCode") });
        }
        */

        try { 
            var result = await _auth.RegisterAsync(dto); 
            _cache.Remove($"RegisterCode_{dto.Email}"); // Clean up
            return Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("request-register-code")]
    public async Task<IActionResult> RequestRegisterCode([FromBody] RequestRegisterCodeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest(new { message = _translator.Get("Auth.EmailRequired") });

        try { await _auth.CheckUniquenessAsync(dto.Email, dto.Phone); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        _cache.Set($"RegisterCode_{dto.Email}", code, TimeSpan.FromMinutes(15));

        var subject = "كود تفعيل حسابك في Sportive";
        var body = $@"
            <div dir='rtl' style='font-family: Arial, sans-serif; border: 1px solid #eee; padding: 20px;'>
                <h2 style='color: #0f3460;'>Sportive Store</h2>
                <p>مرحباً بك في Sportive،</p>
                <p>كود تفعيل حسابك الجديد هو:</p>
                <div style='background: #f4f4f4; padding: 15px; font-size: 24px; font-weight: bold; text-align: center; border-radius: 5px;'>{code}</div>
                <p>هذا الكود صالح لمدة 15 دقيقة.</p>
            </div>";
        
        BackgroundJob.Enqueue<IEmailService>(email => email.SendEmailAsync(dto.Email, subject, body));

        bool isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        return Ok(new { 
            message = _translator.Get("Auth.CodeSent"),
            code = isDev ? code : null 
        });
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        try { 
            var response = await _auth.LoginAsync(dto);
            var user = await _userManager.FindByEmailAsync(dto.Identifier ?? "") 
                       ?? await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Identifier);
            
            await _audit.LogAsync("Login", "User", null, $"User logged in via {dto.Identifier}", user?.Id, user?.FullName);
            return Ok(response); 
        }
        catch (UnauthorizedAccessException ex) 
        {
            var user = await _userManager.FindByEmailAsync(dto.Identifier ?? "") 
                       ?? await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Identifier);

            var correlationId = HttpContext.TraceIdentifier;
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var userAgent = Request.Headers["User-Agent"].ToString() ?? "Unknown";

            await _securityEngine.TrackEventAsync(
                user?.Id,
                ip,
                userAgent,
                SecurityEventType.FailedLogin,
                SecuritySeverity.Low,
                5,
                correlationId
            );

            return Unauthorized(new { message = ex.Message }); 
        }
    }

    [HttpGet("seed-admin")]
    public async Task<IActionResult> SeedAdmin([FromServices] RoleManager<IdentityRole> roleManager)
    {
        var role = "SuperAdmin";
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }

        var user = await _userManager.FindByEmailAsync("admin@raakiza.com");
        if (user == null)
        {
            user = new AppUser
            {
                UserName = "staff_admin@raakiza.com",
                Email = "admin@raakiza.com",
                FullName = "Super Admin",
                PhoneNumber = "01000000000",
                IsActive = true,
                CreatedAt = TimeHelper.GetEgyptTime()
            };
            await _userManager.CreateAsync(user, "Admin@123456");
            await _userManager.AddToRoleAsync(user, role);
            return Ok(new { message = "تم إنشاء حساب السوبر أدمن (admin@raakiza.com) بنجاح!" });
        }
        return Ok(new { message = "الحساب موجود مسبقاً!" });
    }

    /// <summary>تجديد الـ access token — الفرونت يبعت refreshToken يجيب access token جديد</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.RefreshToken))
            return BadRequest(new { message = _translator.Get("Auth.RefreshTokenRequired") });
        try { return Ok(await _auth.RefreshTokenAsync(dto.RefreshToken)); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
    }

    /// <summary>تسجيل خروج — يلغي الـ refresh token</summary>
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        await _auth.RevokeRefreshTokenAsync(userId);
        return Ok(new { message = _translator.Get("Auth.LoggedOut") });
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Identifier ?? "") 
                   ?? await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Identifier);
        
        if (user == null)
            return NotFound(new { message = _translator.Get("Auth.UserNotFound") });

        // ✅ FIX: استخدام RandomNumberGenerator الآمن بدلاً من new Random() غير الآمن
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        _cache.Set($"ResetCode_{dto.Identifier}", code, TimeSpan.FromMinutes(10));

        // Ã°Å¸â€ºÂ¡Ã¯Â¸Â SECURITY FIX: Only send via email/WhatsApp, never return in production!
        if (!string.IsNullOrEmpty(user.Email))
        {
            var subject = _translator.Get("Auth.ResetEmailSubject");
            var body = $@"
                <div dir='rtl' style='font-family: Arial, sans-serif; border: 1px solid #eee; padding: 20px;'>
                    <h2 style='color: #0f3460;'>Sportive Store</h2>
                    <p>{_translator.Get("Auth.ResetEmailGreeting", user.FullName)}</p>
                    <p>{_translator.Get("Auth.ResetEmailCodeText")}</p>
                    <div style='background: #f4f4f4; padding: 15px; font-size: 24px; font-weight: bold; text-align: center; border-radius: 5px;'>{code}</div>
                    <p>{_translator.Get("Auth.ResetEmailExpiryText")}</p>
                    <p>{_translator.Get("Auth.ResetEmailIgnoreText")}</p>
                </div>";
            
            BackgroundJob.Enqueue<IEmailService>(email => email.SendEmailAsync(user.Email, subject, body));
        }

        bool isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        
        return Ok(new { 
            message = _translator.Get("Auth.CodeSent"),
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
            return BadRequest(new { message = _translator.Get("Auth.InvalidCode") });
        }

        var user = await _userManager.FindByEmailAsync(dto.Identifier ?? "") 
                   ?? await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Identifier);

        if (user == null) return NotFound(new { message = _translator.Get("Auth.UserNoLongerExists") });

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);

        if (!result.Succeeded)
            return BadRequest(new { message = string.Join(", ", result.Errors.Select(e => e.Description)) });

        _cache.Remove($"ResetCode_{dto.Identifier}");
        return Ok(new { message = _translator.Get("Auth.PasswordResetSuccess") });
    }

    [HttpPost("send-otp")]
    [EnableRateLimiting("auth")]
    public IActionResult SendOtp([FromBody] SendOtpDto dto)
    {
        // ✅ FIX: استخدام RandomNumberGenerator الآمن
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        // حفظ الكود لمدة 5 دقائق
        _cache.Set($"OtpCode_{dto.PhoneNumber}", code, TimeSpan.FromMinutes(5));

        bool isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        
        // إرسال رسالة باستخدام الواتساب
        BackgroundJob.Enqueue<IWhatsAppApiService>(api => api.SendOtpAsync(dto.PhoneNumber, code));

        return Ok(new { 
            message = _translator.Get("Auth.OtpSent")
        });
    }

    [HttpPost("verify-otp")]
    [EnableRateLimiting("auth")]
    public IActionResult VerifyOtp([FromBody] VerifyOtpDto dto)
    {
        if (!_cache.TryGetValue($"OtpCode_{dto.PhoneNumber}", out string? cachedCode) || cachedCode != dto.Code)
        {
            return BadRequest(new { message = _translator.Get("Auth.InvalidCode") });
        }

        // في حال النجاح نقوم بمسح الكود من الـ Cache
        _cache.Remove($"OtpCode_{dto.PhoneNumber}");
        return Ok(new { message = _translator.Get("Auth.OtpVerified") });
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        var result = await _auth.ChangePasswordAsync(userId, dto);
        return result ? Ok(new { message = _translator.Get("Auth.PasswordChanged") }) : BadRequest(new { message = _translator.Get("Auth.PasswordChangeFailed") });
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
            return NotFound(new { message = _translator.Get("Auth.CustomerNotFound") });

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

        var user = await _userManager.FindByIdAsync(userId);
        var permissionsJson = user?.PermissionsJson;
        
        var overrides = new List<UserModulePermission>();

        // 2. Override with new JSON permissions system if available
        if (!string.IsNullOrEmpty(permissionsJson))
        {
            var jsonPerms = System.Text.Json.JsonSerializer.Deserialize<List<string>>(permissionsJson);
            if (jsonPerms != null)
            {
                // Add any JSON permissions that aren't already in the baseline
                foreach (var p in jsonPerms)
                {
                    if (!permissions.Contains(p)) permissions.Add(p);
                }
            }
        }
        else
        {
            // Fallback for old system (UserModulePermissions table)
            overrides = await _db.UserModulePermissions.Where(p => p.UserAccountID == userId).ToListAsync();
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
        }

        var customer = await _db.Customers
            .Where(c => c.AppUserId == userId)
            .Select(c => new { c.Id, c.Phone })
            .FirstOrDefaultAsync();

        // 3. Branch & Warehouse — resolved from user record first, fallback to employee link
        int? branchId = user?.BranchId;
        int? warehouseId = user?.WarehouseId;
        string? branchName = null;
        string? warehouseName = null;

        if (branchId.HasValue)
        {
            branchName = await _db.Branches.Where(b => b.Id == branchId.Value).Select(b => b.Name).FirstOrDefaultAsync();
            if (warehouseId.HasValue)
            {
                warehouseName = await _db.Warehouses.Where(w => w.Id == warehouseId.Value).Select(w => w.Name).FirstOrDefaultAsync();
            }
        }
        else
        {
            var employee = await _db.Employees
                .Where(e => e.AppUserId == userId && e.BranchId != null)
                .Select(e => new { e.BranchId, BranchName = e.Branch != null ? e.Branch.Name : null })
                .FirstOrDefaultAsync();

            if (employee?.BranchId != null)
            {
                branchId = employee.BranchId;
                branchName = employee.BranchName;

                // Auto-pick the first active warehouse in this branch
                var warehouse = await _db.Warehouses
                    .Where(w => w.BranchId == branchId && w.IsActive)
                    .OrderBy(w => w.Id)
                    .Select(w => new { w.Id, w.Name })
                    .FirstOrDefaultAsync();

                if (warehouse != null)
                {
                    warehouseId = warehouse.Id;
                    warehouseName = warehouse.Name;
                }
            }
        }

        return Ok(new {
            userId,
            email,
            fullName,
            roles,
            permissions,
            customerId = customer?.Id,
            phone      = customer?.Phone,
            pinnedSidebarItems = user?.PinnedSidebarItems ?? "[]",
            favoriteReports = user?.FavoriteReports ?? "[]",
            uiPreferences = user?.UiPreferences ?? "{}",
            modulePermissions = overrides.Select(p => new { p.ModuleKey, p.CanView, p.CanEdit }).ToList(),
            // 🏢 Branch & Warehouse context for POS and stock operations
            branchId,
            branchName,
            warehouseId,
            warehouseName,
            maxDiscountPercentage = user?.MaxDiscountPercentage,
            maxDiscountAmount = user?.MaxDiscountAmount
        });
    }

    [Authorize]
    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePreferencesDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value!;
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound(new { message = _translator.Get("Auth.UserNotFound") });

        if (dto.PinnedSidebarItems != null)
        {
            user.PinnedSidebarItems = dto.PinnedSidebarItems;
        }
        if (dto.FavoriteReports != null)
        {
            user.FavoriteReports = dto.FavoriteReports;
        }
        if (dto.UiPreferences != null)
        {
            user.UiPreferences = dto.UiPreferences;
        }

        await _db.SaveChangesAsync();

        return Ok(new { message = "Preferences updated successfully" });
    }

    [RequirePermission(ModuleKeys.Staff)]
    [HttpGet("staff")]
    public async Task<IActionResult> GetStaff()
    {
        var staffRoles = AppRoles.StaffRoles;
        
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
    [RequirePermission(ModuleKeys.Staff, requireEdit: true)]
    [HttpPost("staff")]
    public async Task<IActionResult> CreateStaff([FromBody] RegisterDto dto, [FromQuery] string role = "Cashier")
    {
        try {
            var existingUser = await _userManager.FindByEmailAsync(dto.Email ?? "")
                               ?? await _userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Phone);
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
                return Ok(new { message = _translator.Get("Auth.StaffReactivated") });
            }

            var authResult = await _auth.RegisterAsync(dto, isCustomer: false);

            // Assign role
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email || u.PhoneNumber == dto.Phone);
            if (user != null)
            {
                await _auth.AssignRoleAsync(user.Id, role);
                await EnsureEmployeeLinkAsync(user, role);
            }
            return Ok(new { message = _translator.Get("Auth.StaffCreated") }); 
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
                var empNo = await sequence.NextAsync("EMP");

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
        
        // ── Admin & Manager: Full Access Baseline ──
        if (Is("Admin") || Is("Manager") || Is("SuperAdmin"))
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
        if (Is("Admin") || Is("SuperAdmin"))
        {
            perms.Add("staff"); perms.Add("staff.edit");
            perms.Add("settings"); perms.Add("settings.edit");
            perms.Add("backup");
            perms.Add("whatsapp");
        }

        // Ã¢â€â‚¬Ã¢â€â‚¬ Cashier: POS & Orders Operations Ã¢â€â‚¬Ã¢â€â‚¬
        if (Is("Cashier"))
        {
            perms.Add("pos");
            perms.Add("pos.returns");
            perms.Add("orders");
            perms.Add("orders.read");
            perms.Add("customers"); // To add/select customers during sale
        }

        // Ã¢â€â‚¬Ã¢â€â‚¬ Accountant: Financial & Reporting Ã¢â€â‚¬Ã¢â€â‚¬
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

        // Ã¢â€â‚¬Ã¢â€â‚¬ Staff (Store Keeper / Sales): Inventory & Orders Ã¢â€â‚¬Ã¢â€â‚¬
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

