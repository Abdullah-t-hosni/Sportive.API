using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;

    public AuthService(UserManager<AppUser> userManager, IConfiguration config, AppDbContext db)
    {
        _userManager = userManager;
        _config = config;
        _db = db;
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto)
    {
        // ── اسم كامل → FirstName + LastName ──────────────
        var parts = dto.FullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts[0];
        var lastName  = parts.Length > 1 ? parts[1] : "-";

        // ── الإيميل: إلزامي للتسجيل لكن يُنشأ تلقائياً لو مش موجود ──
        var email = string.IsNullOrWhiteSpace(dto.Email)
            ? $"pos_{dto.Phone}@sportive.pos"
            : dto.Email.Trim().ToLower();

        // ── تحقق التكرار ──────────────────────────────────
        if (await _userManager.FindByEmailAsync(email) != null)
            throw new InvalidOperationException("هذا الإيميل مسجل مسبقاً");

        var phoneExists = await _userManager.Users
            .AnyAsync(u => u.PhoneNumber == dto.Phone);
        if (phoneExists)
            throw new InvalidOperationException("رقم الهاتف مسجل مسبقاً");

        var user = new AppUser
        {
            UserName    = email,
            Email       = email,
            FirstName   = firstName,
            LastName    = lastName,
            PhoneNumber = dto.Phone,
            IsActive    = true
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(user, "Customer");

        // ── Customer Profile ──────────────────────────────
        _db.Customers.Add(new Customer
        {
            FirstName = firstName,
            LastName  = lastName,
            Email     = email,
            Phone     = dto.Phone,
            AppUserId = user.Id,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return await BuildTokenResponse(user);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginDto dto)
    {
        AppUser? user = null;

        // ── تسجيل الدخول برقم التليفون (الأساسي) ─────────
        if (!string.IsNullOrWhiteSpace(dto.Phone))
        {
            user = await _userManager.Users
                .FirstOrDefaultAsync(u => u.PhoneNumber == dto.Phone);
        }

        // ── أو بالإيميل (احتياطي) ─────────────────────────
        if (user == null && !string.IsNullOrWhiteSpace(dto.Email))
        {
            user = await _userManager.FindByEmailAsync(dto.Email);
        }

        if (user == null)
            throw new UnauthorizedAccessException("رقم الهاتف أو الإيميل غير صحيح");

        if (!await _userManager.CheckPasswordAsync(user, dto.Password))
            throw new UnauthorizedAccessException("كلمة المرور غير صحيحة");

        if (!user.IsActive)
            throw new UnauthorizedAccessException("الحساب موقوف");

        return await BuildTokenResponse(user);
    }

    public async Task<bool> ChangePasswordAsync(string userId, ChangePasswordDto dto)
    {
        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found");
        var result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
        return result.Succeeded;
    }

    public async Task<bool> AssignRoleAsync(string userId, string role)
    {
        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found");
        return (await _userManager.AddToRoleAsync(user, role)).Succeeded;
    }

    private async Task<AuthResponseDto> BuildTokenResponse(AppUser user)
    {
        var roles    = await _userManager.GetRolesAsync(user);
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.AppUserId == user.Id && !c.IsDeleted);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email,          user.Email!),
            new(ClaimTypes.GivenName,      user.FirstName),
            new(ClaimTypes.Surname,        user.LastName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["JWT:Secret"]!));
        var creds   = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddHours(int.Parse(_config["JWT:ExpiresHours"] ?? "24"));

        var token = new JwtSecurityToken(
            issuer:            _config["JWT:Issuer"],
            audience:          _config["JWT:Audience"],
            claims:            claims,
            expires:           expires,
            signingCredentials: creds);

        return new AuthResponseDto(
            Token:        new JwtSecurityTokenHandler().WriteToken(token),
            RefreshToken: Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            Email:        user.Email!,
            FullName:     $"{user.FirstName} {user.LastName}",
            Roles:        roles,
            ExpiresAt:    expires,
            CustomerId:   customer?.Id
        );
    }
}
