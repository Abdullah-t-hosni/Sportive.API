using System;
using System.Threading.Tasks;
using Sportive.API.DTOs.System;
using Sportive.API.Models;

namespace Sportive.API.Interfaces;

public interface ITenantService
{
    Task<TenantOnboardingResult> OnboardNewTenantAsync(OnboardTenantRequest request);
    Task<SelfRegisterResult> SelfRegisterAsync(SelfRegisterRequest request);
    Task<bool> IsSlugAvailableAsync(string slug);
    Task<PagedResponseDto<TenantListDto>> GetAllTenantsAsync(TenantQueryDto query);
    Task<TenantDetailsDto?> GetTenantByIdAsync(Guid tenantGuid);
    Task<TenantUsageDto?> GetTenantUsageAsync(Guid id);
    Task<(bool Success, string Message)> UpdateTenantAsync(Guid tenantGuid, UpdateTenantDto request);
    Task<(bool Success, string Message)> LockTenantAsync(Guid id, string? reason);
    Task<(bool Success, string Message)> UnlockTenantAsync(Guid id);
}

