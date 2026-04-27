using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Utils;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Sportive.API.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;
    private readonly ICustomerService _customerService;

    public AuthService(
        UserManager<AppUser> userManager, 
        RoleManager<IdentityRole> roleManager, 
        IConfiguration config,
        AppDbContext db,
        ICustomerService customerService)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _config = config;
        _db = db;
        _customerService = customerService;
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto, bool isCustomer = true)
    {
        var prefix = isCustomer ? "cust_" : "staff_";

        // 1. Validate Uniqueness
        if (!string.IsNullOrEmpty(dto.Email))
        {
            var existingEmail = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == dto.Email && u.UserName!.StartsWith(prefix));
            if (existingEmail != null) 
                throw new InvalidOperationException("البريد الإلكتروني مستخدم بالفعل / Email already in use.");
        }

        if (!string.IsNullOrEmpty(dto.Phone))
        {
            var existingPhone = await _userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Phone && u.UserName!.StartsWith(prefix));
            if (existingPhone != null) 
                throw new InvalidOperationException("رقم الهاتف مستخدم بالفعل / Phone number already in use.");
        }

        // 2. Create User
        // الـ UserName دايماً هايكون الموبايل كـ Unique identifier أساسي لو الميل مش موجود
        var userName = prefix + (!string.IsNullOrEmpty(dto.Phone) ? dto.Phone : dto.Email);
        
        var user = new AppUser
        {
            UserName = userName,
            Email = !string.IsNullOrEmpty(dto.Email) ? dto.Email : null,
            PhoneNumber = dto.Phone,
            FullName = dto.FullName,
            CreatedAt = TimeHelper.GetEgyptTime(),
            IsActive = true
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            var msg = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException(msg);
        }

        if (isCustomer)
        {
            if (!await _roleManager.RoleExistsAsync("Customer"))
                await _roleManager.CreateAsync(new IdentityRole("Customer"));
            
            await _userManager.AddToRoleAsync(user, "Customer");
        }

        if (isCustomer)
        {
            // 3. Link or Create Customer record
            var customer = await _db.Customers
                .FirstOrDefaultAsync(c => c.Phone == dto.Phone);

            if (customer != null)
            {
                customer.AppUserId = user.Id;
                customer.Email = user.Email ?? customer.Email; // Sync email if provided
                customer.FullName = user.FullName;
                customer.UpdatedAt = TimeHelper.GetEgyptTime();
            }
            else
            {
                customer = new Customer
                {
                    AppUserId = user.Id,
                    FullName = user.FullName,
                    Email = user.Email ?? "",
                    Phone = user.PhoneNumber,
                    CreatedAt = TimeHelper.GetEgyptTime()
                };
                _db.Customers.Add(customer);
            }
            await _db.SaveChangesAsync();
            
            // ── Auto-create Account ──
            await _customerService.EnsureCustomerAccountAsync(customer.Id);
        }

        return await LoginInternalAsync(user);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginDto dto)
    {
        if (string.IsNullOrEmpty(dto.Identifier))
            throw new UnauthorizedAccessException("رقم الهاتف أو البريد الإلكتروني مطلوب");

        var prefix = dto.IsStaff ? "staff_" : "cust_";

        // البحث بالبريد أو الهاتف أو اسم المستخدم
        var user = await _userManager.Users
            .FirstOrDefaultAsync(u => 
                (u.Email == dto.Identifier || u.PhoneNumber == dto.Identifier || u.UserName == dto.Identifier || u.UserName == prefix + dto.Identifier)
                && (u.UserName!.StartsWith(prefix) || u.Email == "admin@sportive.com" || u.UserName == dto.Identifier));

        if (user == null || !user.IsActive || !await _userManager.CheckPasswordAsync(user, dto.Password))
            throw new UnauthorizedAccessException("بيانات الدخول غير صحيحة");

        return await LoginInternalAsync(user);
    }

    private async Task<AuthResponseDto> LoginInternalAsync(AppUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email ?? ""),
            new Claim(ClaimTypes.Name, user.FullName)
        };
        foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));

        // Get customer ID
        var customerId = await _db.Customers
            .Where(c => c.AppUserId == user.Id)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync();

        if (customerId.HasValue)
            claims.Add(new Claim("CustomerId", customerId.Value.ToString()));

        // Fetch addresses if customer
        var addresses = customerId.HasValue 
            ? await _db.Addresses.Where(a => a.CustomerId == customerId.Value)
                .Select(a => new AddressDto(a.Id, a.TitleAr, a.TitleEn, a.Street, a.City, a.District, a.BuildingNo, a.Floor, a.ApartmentNo, a.IsDefault, a.Latitude, a.Longitude))
                .ToListAsync()
            : null;

        // Fetch permissions override
        var permissions = await _db.UserModulePermissions.Where(p => p.UserAccountID == user.Id)
            .Select(p => new ModulePermissionDto(p.ModuleKey, p.CanView, p.CanEdit))
            .ToListAsync();

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["JWT:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddHours(double.Parse(_config["JWT:ExpiresHours"] ?? "72"));

        var token = new JwtSecurityToken(
            issuer: _config["JWT:Issuer"],
            audience: _config["JWT:Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        // توليد refresh token وتخزينه في DB
        var refreshToken = GenerateSecureRefreshToken();
        user.RefreshToken       = refreshToken;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(30);
        await _userManager.UpdateAsync(user);

        return new AuthResponseDto(
            user.Id,
            new JwtSecurityTokenHandler().WriteToken(token),
            refreshToken,
            user.Email ?? "",
            user.FullName,
            roles,
            expires,
            customerId,
            user.PhoneNumber,
            addresses,
            permissions
        );
    }

    /// <summary>تجديد الـ access token باستخدام refresh token صالح</summary>
    public async Task<AuthResponseDto> RefreshTokenAsync(string refreshToken)
    {
        var user = await _userManager.Users
            .FirstOrDefaultAsync(u => u.RefreshToken == refreshToken);

        if (user == null || !user.IsActive
            || user.RefreshTokenExpiry == null
            || user.RefreshTokenExpiry < DateTime.UtcNow)
            throw new UnauthorizedAccessException("رمز التجديد غير صالح أو منتهي الصلاحية.");

        return await LoginInternalAsync(user);
    }

    /// <summary>إلغاء الـ refresh token (تسجيل خروج)</summary>
    public async Task RevokeRefreshTokenAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return;
        user.RefreshToken       = null;
        user.RefreshTokenExpiry = null;
        await _userManager.UpdateAsync(user);
    }

    /// <summary>
    /// ✅ يولّد Refresh Token آمن باستخدام RandomNumberGenerator
    /// ملاحظة: في الإصدار القادم يجب تخزينه في DB وربطه بالمستخدم لدعم الإلغاء الكامل
    /// </summary>
    private static string GenerateSecureRefreshToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    public async Task<bool> ChangePasswordAsync(string userId, ChangePasswordDto dto)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;
        var result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
        return result.Succeeded;
    }

    public async Task<bool> AssignRoleAsync(string userId, string role)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        if (!await _roleManager.RoleExistsAsync(role))
            await _roleManager.CreateAsync(new IdentityRole(role));

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);
        var result = await _userManager.AddToRoleAsync(user, role);
        return result.Succeeded;
    }
}
