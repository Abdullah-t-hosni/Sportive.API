// ============================================================
// DTOs/AuthDtos.cs
// تم الفصل من Dtos.cs الكبير لتحسين التنظيم والصيانة
// ============================================================
namespace Sportive.API.DTOs;

// ========== AUTH ==========
public record RegisterDto(
    string FullName,
    string? Email,
    string Password,
    string? Phone
);

public record LoginDto(string Identifier, string Password);

public record AuthResponseDto(
    string UserId,
    string Token,
    string RefreshToken,
    string Email,
    string FullName,
    IList<string> Roles,
    DateTime ExpiresAt,
    int? CustomerId = null,
    string? Phone = null,
    List<AddressDto>? Addresses = null,
    List<ModulePermissionDto>? ModulePermissions = null
);

public record ModulePermissionDto(string ModuleKey, bool CanView, bool CanEdit);

public record ChangePasswordDto(string CurrentPassword, string NewPassword);
public record ForgotPasswordDto(string Identifier);
public record ResetPasswordDto(string Identifier, string Code, string NewPassword);

public record RefreshTokenRequestDto(string RefreshToken);

// ========== OTP VIA WHATSAPP ==========
public record SendOtpDto(string PhoneNumber);
public record VerifyOtpDto(string PhoneNumber, string Code);
