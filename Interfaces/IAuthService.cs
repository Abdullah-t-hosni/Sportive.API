using Sportive.API.DTOs;

namespace Sportive.API.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> RegisterAsync(RegisterDto dto, bool isCustomer = true);
    Task<AuthResponseDto> LoginAsync(LoginDto dto);
    Task<bool> ChangePasswordAsync(string userId, ChangePasswordDto dto);
    Task<bool> AssignRoleAsync(string userId, string role);
    Task<AuthResponseDto> RefreshTokenAsync(string refreshToken);
    Task RevokeRefreshTokenAsync(string userId);
    Task CheckUniquenessAsync(string? email, string? phone);
}
