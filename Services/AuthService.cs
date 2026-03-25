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
        var existing = await _userManager.FindByEmailAsync(dto.Email);
        if (existing != null)
            throw new InvalidOperationException("Email already registered");

        if (!string.IsNullOrWhiteSpace(dto.Phone))
        {
            var phoneExists = await _userManager.Users.AnyAsync(u => u.PhoneNumber == dto.Phone);
            if (phoneExists)
                throw new InvalidOperationException("رقم الهاتف مسجل مسبقاً بحساب آخر. الرجاء استخدام رقم مختلف أو تسجيل الدخول.");
        }

        var user = new AppUser
        {
            UserName = dto.Email,
            Email = dto.Email,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            PhoneNumber = dto.Phone
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException(errors);
        }

        await _userManager.AddToRoleAsync(user, "Customer");

        // Create customer profile linked to this user
        var customer = new Customer
        {
            FirstName   = dto.FirstName,
            LastName    = dto.LastName,
            Email       = dto.Email,
            Phone       = dto.Phone,
            AppUserId   = user.Id,
            CreatedAt   = DateTime.UtcNow
        };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        return await BuildTokenResponse(user);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email)
            ?? throw new UnauthorizedAccessException("Invalid email or password");

        if (!await _userManager.CheckPasswordAsync(user, dto.Password))
            throw new UnauthorizedAccessException("Invalid email or password");

        if (!user.IsActive)
            throw new UnauthorizedAccessException("Account is disabled");

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
        var result = await _userManager.AddToRoleAsync(user, role);
        return result.Succeeded;
    }

    private async Task<AuthResponseDto> BuildTokenResponse(AppUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.AppUserId == user.Id && !c.IsDeleted);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email!),
            new(ClaimTypes.GivenName, user.FirstName),
            new(ClaimTypes.Surname, user.LastName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["JWT:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddHours(
            int.Parse(_config["JWT:ExpiresHours"] ?? "24"));

        var token = new JwtSecurityToken(
            issuer: _config["JWT:Issuer"],
            audience: _config["JWT:Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        var refreshToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        return new AuthResponseDto(
            new JwtSecurityTokenHandler().WriteToken(token),
            refreshToken,
            user.Email!,
            $"{user.FirstName} {user.LastName}",
            roles,
            expires,
            customer?.Id
        );
    }
}
