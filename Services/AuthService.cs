using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly ICustomerService _customers;
    private readonly IConfiguration _config;

    public AuthService(UserManager<AppUser> userManager, ICustomerService customers, IConfiguration config)
    {
        _userManager = userManager;
        _customers   = customers;
        _config      = config;
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto)
    {
        var existing = await _userManager.FindByEmailAsync(dto.Email);
        if (existing != null)
            throw new InvalidOperationException("Email already registered");

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

    public async Task<int?> GetMyCustomerIdAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found");
        var roles = await _userManager.GetRolesAsync(user);
        if (!roles.Contains("Customer"))
            return null;
        return await _customers.EnsureCustomerProfileAsync(user);
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

        int? customerId = null;
        if (roles.Contains("Customer"))
            customerId = await _customers.EnsureCustomerProfileAsync(user);

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
            customerId
        );
    }
}
