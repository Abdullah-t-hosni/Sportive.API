using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Sportive.API.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;

    public AuthService(
        UserManager<AppUser> userManager, 
        RoleManager<IdentityRole> roleManager, 
        IConfiguration config,
        AppDbContext db)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _config = config;
        _db = db;
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto)
    {
        // 1. Validate Uniqueness
        if (!string.IsNullOrEmpty(dto.Email))
        {
            var existingEmail = await _userManager.FindByEmailAsync(dto.Email);
            if (existingEmail != null) 
                throw new InvalidOperationException("البريد الإلكتروني مستخدم بالفعل / Email already in use.");
        }

        if (!string.IsNullOrEmpty(dto.Phone))
        {
            var existingPhone = await _userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Phone);
            if (existingPhone != null) 
                throw new InvalidOperationException("رقم الهاتف مستخدم بالفعل / Phone number already in use.");
        }

        // 2. Create User
        // الـ UserName دايماً هايكون الموبايل كـ Unique identifier أساسي لو الميل مش موجود
        var userName = !string.IsNullOrEmpty(dto.Phone) ? dto.Phone : dto.Email;
        
        var user = new AppUser
        {
            UserName = userName,
            Email = !string.IsNullOrEmpty(dto.Email) ? dto.Email : null,
            PhoneNumber = dto.Phone,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            var msg = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException(msg);
        }

        // Default role: Customer
        if (!await _roleManager.RoleExistsAsync("Customer"))
            await _roleManager.CreateAsync(new IdentityRole("Customer"));
        
        await _userManager.AddToRoleAsync(user, "Customer");

        // 3. Link or Create Customer record
        var customer = await _db.Customers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Phone == dto.Phone);

        if (customer != null)
        {
            customer.AppUserId = user.Id;
            customer.Email = user.Email ?? customer.Email; // Sync email if provided
            customer.FirstName = user.FirstName;
            customer.LastName = user.LastName;
            customer.IsDeleted = false; // "Re-activate" if it was soft-deleted
            customer.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            customer = new Customer
            {
                AppUserId = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email ?? "",
                Phone = user.PhoneNumber,
                CreatedAt = DateTime.UtcNow
            };
            _db.Customers.Add(customer);
        }
        
        await _db.SaveChangesAsync();

        return await LoginInternalAsync(user);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginDto dto)
    {
        if (string.IsNullOrEmpty(dto.Identifier))
            throw new UnauthorizedAccessException("رقم الهاتف أو البريد الإلكتروني مطلوب");

        // البحث بالبريد أو الهاتف أو اسم المستخدم
        var user = await _userManager.Users
            .FirstOrDefaultAsync(u => u.Email == dto.Identifier 
                                   || u.PhoneNumber == dto.Identifier 
                                   || u.UserName == dto.Identifier);

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
            new Claim(ClaimTypes.GivenName, user.FirstName),
            new Claim(ClaimTypes.Surname, user.LastName)
        };
        foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));

        // Get customer ID
        var customerId = await _db.Customers
            .Where(c => c.AppUserId == user.Id && !c.IsDeleted)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync();

        if (customerId.HasValue)
            claims.Add(new Claim("CustomerId", customerId.Value.ToString()));

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

        return new AuthResponseDto(
            user.Id,
            new JwtSecurityTokenHandler().WriteToken(token),
            Guid.NewGuid().ToString(), // Mock refresh token
            user.Email ?? "",
            $"{user.FirstName} {user.LastName}",
            roles,
            expires,
            customerId
        );
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
