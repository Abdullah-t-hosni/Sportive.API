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
    private readonly ITranslator _t;
    private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _httpContextAccessor;
    private readonly EncryptionHelper _encryptionHelper;

    public AuthService(
        UserManager<AppUser> userManager, 
        RoleManager<IdentityRole> roleManager, 
        IConfiguration config,
        AppDbContext db,
        ICustomerService customerService,
        ITranslator t,
        Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor,
        EncryptionHelper encryptionHelper)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _config = config;
        _db = db;
        _customerService = customerService;
        _t = t;
        _httpContextAccessor = httpContextAccessor;
        _encryptionHelper = encryptionHelper;
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto, bool isCustomer = true)
    {
        // 1. Validate Uniqueness
        await CheckUniquenessAsync(dto.Email, dto.Phone);

        // 2. Create User (Unified without prefixes)
        var userName = !string.IsNullOrEmpty(dto.Email) ? dto.Email : dto.Phone;
        
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
            var msg = string.Join(", ", result.Errors.Select(e => _t.Get("Auth." + e.Code)));
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
            var phoneHash = !string.IsNullOrEmpty(dto.Phone) ? _encryptionHelper.ComputeSearchHash(dto.Phone) : "";
            var customer = await _db.Customers
                .FirstOrDefaultAsync(c => c.PhoneHash == phoneHash);

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
            throw new UnauthorizedAccessException(_t.Get("Auth.IdentifierRequired"));

        // البحث الذكي الموحد: يبحث عن الإيميل، الهاتف، أو اسم المستخدم (النظيف أو القديم بالبادئات)
        var user = await _userManager.Users
            .FirstOrDefaultAsync(u => 
                u.Email == dto.Identifier || 
                u.PhoneNumber == dto.Identifier || 
                u.UserName == dto.Identifier || 
                u.UserName == "staff_" + dto.Identifier || 
                u.UserName == "cust_" + dto.Identifier);

        if (user == null || !user.IsActive)
            throw new UnauthorizedAccessException(_t.Get("Auth.InvalidCredentials"));

        if (await _userManager.IsLockedOutAsync(user))
            throw new UnauthorizedAccessException(_t.Get("Auth.AccountLocked") ?? "تم قفل الحساب مؤقتاً لكثرة المحاولات الخاطئة. يرجى المحاولة لاحقاً.");

        if (!await _userManager.CheckPasswordAsync(user, dto.Password))
        {
            await _userManager.AccessFailedAsync(user);
            throw new UnauthorizedAccessException(_t.Get("Auth.InvalidCredentials"));
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        return await LoginInternalAsync(user);
    }

    private async Task<AuthResponseDto> LoginInternalAsync(AppUser user, UserSession? existingSession = null)
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

        if (!customerId.HasValue)
        {
            // 🛡️ Auto-create customer record ONLY if user is not staff
            var staffRoles = AppRoles.StaffRoles.ToList();
            bool isStaff = roles.Any(r => staffRoles.Contains(r));

            if (!isStaff)
            {
                // Fix: Use phone or a unique fallback to avoid "" email 409 conflict
                var fallbackEmail = !string.IsNullOrEmpty(user.Email) ? user.Email : 
                                   (!string.IsNullOrEmpty(user.PhoneNumber) ? $"{user.PhoneNumber}@sportive.com" : $"{user.Id.Substring(0, 8)}@temp.sportive.com");

                // Ensure uniqueness even for fallback
                var fallbackEmailHash = _encryptionHelper.ComputeSearchHash(fallbackEmail);
                if (await _db.Customers.AnyAsync(c => c.EmailHash == fallbackEmailHash))
                {
                    fallbackEmail = $"{Guid.NewGuid().ToString().Substring(0, 8)}@temp.sportive.com";
                }

                var customer = new Customer
                {
                    AppUserId = user.Id,
                    FullName = user.FullName,
                    Email = fallbackEmail,
                    Phone = user.PhoneNumber,
                    CreatedAt = TimeHelper.GetEgyptTime()
                };
                _db.Customers.Add(customer);
                await _db.SaveChangesAsync();
                customerId = customer.Id;

                // Auto-create Accounting Account
                await _customerService.EnsureCustomerAccountAsync(customer.Id);
            }
        }

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

        // ── Generate User Session ──
        var refreshToken = GenerateSecureRefreshToken();
        var hashedRefreshToken = HashRefreshToken(refreshToken);
        Guid sessionId;

        if (existingSession != null)
        {
            var (deviceName, fingerprint) = ParseDeviceAndFingerprint();
            existingSession.RefreshTokenHash = hashedRefreshToken;
            existingSession.ExpiresAt = DateTime.UtcNow.AddDays(30);
            existingSession.LastSeen = TimeHelper.GetEgyptTime();
            existingSession.IpAddress = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
            existingSession.DeviceName = deviceName;
            existingSession.DeviceFingerprint = fingerprint;
            existingSession.UserAgent = _httpContextAccessor.HttpContext?.Request?.Headers["User-Agent"].ToString() ?? "";
            existingSession.IsRevoked = false;
            existingSession.RevokedAt = null;

            _db.UserSessions.Update(existingSession);
            await _db.SaveChangesAsync();
            sessionId = existingSession.Id;
        }
        else
        {
            var (deviceName, fingerprint) = ParseDeviceAndFingerprint();
            var newSession = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                DeviceName = deviceName,
                DeviceFingerprint = fingerprint,
                UserAgent = _httpContextAccessor.HttpContext?.Request?.Headers["User-Agent"].ToString() ?? "",
                IpAddress = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown",
                CreatedAt = TimeHelper.GetEgyptTime(),
                LastSeen = TimeHelper.GetEgyptTime(),
                ExpiresAt = DateTime.UtcNow.AddDays(30),
                RefreshTokenHash = hashedRefreshToken,
                IsRevoked = false
            };

            _db.UserSessions.Add(newSession);
            await _db.SaveChangesAsync();
            sessionId = newSession.Id;
        }

        claims.Add(new Claim("SessionId", sessionId.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["JWT:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        // ⚠️ Access Token: short-lived (2h default). Refresh token handles re-auth silently.
        var accessTokenHours = double.Parse(_config["JWT:ExpiresHours"] ?? "2");
        var expires = DateTime.UtcNow.AddHours(accessTokenHours);

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
            refreshToken,
            user.Email ?? "",
            user.FullName,
            roles,
            expires,
            customerId,
            user.PhoneNumber,
            addresses,
            permissions,
            user.PinnedSidebarItems,
            user.FavoriteReports,
            user.UiPreferences
        );
    }

    /// <summary>تجديد الـ access token باستخدام refresh token صالح</summary>
    public async Task<AuthResponseDto> RefreshTokenAsync(string refreshToken)
    {
        var hashedToken = HashRefreshToken(refreshToken);

        // 1. Search in UserSessions
        var session = await _db.UserSessions
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == hashedToken);

        if (session != null)
        {
            if (session.IsRevoked || (session.ExpiresAt != null && session.ExpiresAt < DateTime.UtcNow))
            {
                if (!session.IsRevoked)
                {
                    session.IsRevoked = true;
                    session.RevokedAt = TimeHelper.GetEgyptTime();
                    await _db.SaveChangesAsync();
                }
                throw new UnauthorizedAccessException(_t.Get("Auth.RefreshTokenInvalid"));
            }

            var user = await _userManager.FindByIdAsync(session.UserId);
            if (user == null || !user.IsActive)
                throw new UnauthorizedAccessException(_t.Get("Auth.RefreshTokenInvalid"));

            return await LoginInternalAsync(user, session);
        }

        // 2. Migration Fallback: Check AspNetUsers (AppUser)
        var fallbackUser = await _userManager.Users
            .FirstOrDefaultAsync(u => u.RefreshTokenHash == hashedToken);

        if (fallbackUser == null)
        {
            fallbackUser = await _userManager.Users
                .FirstOrDefaultAsync(u => u.RefreshTokenHash == refreshToken);
        }

        if (fallbackUser != null)
        {
            if (fallbackUser.IsActive && fallbackUser.RefreshTokenExpiry != null && fallbackUser.RefreshTokenExpiry >= DateTime.UtcNow)
            {
                // Create a new session for this migrated user session
                var (deviceName, fingerprint) = ParseDeviceAndFingerprint();
                var newSession = new UserSession
                {
                    Id = Guid.NewGuid(),
                    UserId = fallbackUser.Id,
                    DeviceName = deviceName,
                    DeviceFingerprint = fingerprint,
                    UserAgent = _httpContextAccessor.HttpContext?.Request?.Headers["User-Agent"].ToString() ?? "",
                    IpAddress = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown",
                    CreatedAt = TimeHelper.GetEgyptTime(),
                    LastSeen = TimeHelper.GetEgyptTime(),
                    ExpiresAt = DateTime.UtcNow.AddDays(30),
                    RefreshTokenHash = hashedToken,
                    IsRevoked = false
                };

                _db.UserSessions.Add(newSession);

                // Clear legacy token fields from user
                fallbackUser.RefreshTokenHash = null;
                fallbackUser.RefreshTokenExpiry = null;
                await _userManager.UpdateAsync(fallbackUser);
                await _db.SaveChangesAsync();

                return await LoginInternalAsync(fallbackUser, newSession);
            }
        }

        throw new UnauthorizedAccessException(_t.Get("Auth.RefreshTokenInvalid"));
    }

    /// <summary>إلغاء الـ refresh token (تسجيل خروج)</summary>
    public async Task RevokeRefreshTokenAsync(string userId)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var sessionIdClaim = httpContext?.User?.FindFirst("SessionId")?.Value;
        if (!string.IsNullOrEmpty(sessionIdClaim) && Guid.TryParse(sessionIdClaim, out var sessionId))
        {
            var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);
            if (session != null)
            {
                session.IsRevoked = true;
                session.RevokedAt = TimeHelper.GetEgyptTime();
                await _db.SaveChangesAsync();
            }
        }
        else
        {
            // Fallback: revoke all active sessions for this user if no current session found
            var activeSessions = await _db.UserSessions
                .Where(s => s.UserId == userId && !s.IsRevoked)
                .ToListAsync();

            foreach (var session in activeSessions)
            {
                session.IsRevoked = true;
                session.RevokedAt = TimeHelper.GetEgyptTime();
            }
            await _db.SaveChangesAsync();
        }

        // Also clean up any legacy user level token
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null)
        {
            user.RefreshTokenHash = null;
            user.RefreshTokenExpiry = null;
            await _userManager.UpdateAsync(user);
        }
    }

    private (string DeviceName, string Fingerprint) ParseDeviceAndFingerprint()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return ("Unknown Device", "");
        }

        var userAgent = httpContext.Request.Headers["User-Agent"].ToString() ?? "";
        var acceptLanguage = httpContext.Request.Headers["Accept-Language"].ToString() ?? "";

        var platform = "Unknown Platform";
        if (userAgent.Contains("Windows")) platform = "Windows";
        else if (userAgent.Contains("Android")) platform = "Android";
        else if (userAgent.Contains("iPhone") || userAgent.Contains("iPad")) platform = "iOS";
        else if (userAgent.Contains("Macintosh") || userAgent.Contains("Mac OS X")) platform = "macOS";
        else if (userAgent.Contains("Linux")) platform = "Linux";

        var browser = "Unknown Browser";
        if (userAgent.Contains("Edg")) browser = "Edge";
        else if (userAgent.Contains("Chrome") && !userAgent.Contains("Chromium")) browser = "Chrome";
        else if (userAgent.Contains("Safari") && !userAgent.Contains("Chrome")) browser = "Safari";
        else if (userAgent.Contains("Firefox")) browser = "Firefox";
        else if (userAgent.Contains("Opera") || userAgent.Contains("OPR")) browser = "Opera";

        var deviceName = $"{browser} {platform}";

        var rawFingerprint = $"{userAgent}|{platform}|{browser}|{acceptLanguage}";
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawFingerprint));
        var fingerprint = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

        return (deviceName, fingerprint);
    }

    private string HashRefreshToken(string token)
    {
        var secret = _config["Security:RefreshTokenSecret"];
        if (string.IsNullOrEmpty(secret) || secret == "${REFRESH_TOKEN_SECRET}")
        {
            secret = Environment.GetEnvironmentVariable("REFRESH_TOKEN_SECRET");
        }
        if (string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException("Security:RefreshTokenSecret is not configured.");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashBytes);
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

    public async Task CheckUniquenessAsync(string? email, string? phone)
    {
        if (!string.IsNullOrEmpty(email))
        {
            var existingEmail = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (existingEmail != null) 
                throw new InvalidOperationException(_t.Get("Auth.EmailInUse"));
        }

        if (!string.IsNullOrEmpty(phone))
        {
            var existingPhone = await _userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone);
            if (existingPhone != null) 
                throw new InvalidOperationException(_t.Get("Auth.PhoneInUse"));
        }
    }
}
