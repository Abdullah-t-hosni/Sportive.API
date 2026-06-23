using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.DTOs.System;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services;

public class TenantService : ITenantService
{
    private readonly MasterDbContext _masterContext;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ITenantContext _tenantContext;
    private readonly UserManager<AppUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TenantService> _logger;

    public TenantService(
        MasterDbContext masterContext,
        IDbContextFactory<AppDbContext> dbContextFactory,
        ITenantContext tenantContext,
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IMemoryCache cache,
        ILogger<TenantService> logger)
    {
        _masterContext = masterContext;
        _dbContextFactory = dbContextFactory;
        _tenantContext = tenantContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _cache = cache;
        _logger = logger;
    }

    public async Task<TenantOnboardingResult> OnboardNewTenantAsync(OnboardTenantRequest request)
    {
        // 0. Validation
        if (await _masterContext.Tenants.AnyAsync(x => x.Slug == request.Slug))
            return new TenantOnboardingResult { Success = false, Message = "Slug already exists" };

        if (await _masterContext.Tenants.AnyAsync(x => x.Subdomain == request.Subdomain))
            return new TenantOnboardingResult { Success = false, Message = "Subdomain already exists" };

        if (await _masterContext.Tenants.AnyAsync(x => x.DatabaseName == request.DatabaseName))
            return new TenantOnboardingResult { Success = false, Message = "Database already assigned" };

        var tenantGuid = Guid.NewGuid();
        var tempPassword = PasswordGenerator.GenerateSecurePassword();
        var adminEmail = $"admin@{request.Slug}.local";

        var newTenant = new Tenant
        {
            TenantGuid = tenantGuid,
            Slug = request.Slug,
            Name = request.Name,
            Subdomain = request.Subdomain,
            DatabaseName = request.DatabaseName,
            DatabaseUser = request.DatabaseUser,
            DatabasePassword = request.DatabasePassword,
            Status = TenantStatus.Active,
            CreatedAt = TimeHelper.GetEgyptTime(),
            IsLocked = false
        };

        using var transaction = await _masterContext.Database.BeginTransactionAsync();
        try
        {
            // Set context so AppDbContext uses the new database connection string
            _tenantContext.SetTenant(newTenant);

            // 1. Check connection
            using var appContext = await _dbContextFactory.CreateDbContextAsync();
            if (!await appContext.Database.CanConnectAsync())
            {
                await transaction.RollbackAsync();
                return new TenantOnboardingResult { Success = false, Message = "Database credentials are invalid or database does not exist." };
            }

            // 2. Create Tenant
            _masterContext.Tenants.Add(newTenant);

            // 3. Create Trial Subscription
            var trialPlan = await _masterContext.Plans.FirstOrDefaultAsync(x => x.Name == "Trial");
            if (trialPlan == null)
            {
                await transaction.RollbackAsync();
                return new TenantOnboardingResult { Success = false, Message = "System Error: Trial plan not found." };
            }

            _masterContext.TenantSubscriptions.Add(new TenantSubscription
            {
                TenantGuid = tenantGuid,
                PlanId = trialPlan.Id,
                StartsAt = TimeHelper.GetEgyptTime(),
                ExpiresAt = TimeHelper.GetEgyptTime().AddDays(14),
                IsActive = true,
                IsTrial = true,
                AutoRenew = false
            });

            await _masterContext.SaveChangesAsync();

            // 4. Migrate App Database
            _logger.LogInformation("Running onboarding migration for tenant {TenantSlug}", newTenant.Slug);
            await appContext.Database.MigrateAsync();

            // 5. Seed SuperAdmin User
            var adminUser = new AppUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "System Administrator",
                EmailConfirmed = true,
                CreatedAt = TimeHelper.GetEgyptTime()
            };

            var userResult = await _userManager.CreateAsync(adminUser, tempPassword);
            if (!userResult.Succeeded)
            {
                await transaction.RollbackAsync();
                return new TenantOnboardingResult { Success = false, Message = $"Failed to create admin user: {string.Join(", ", userResult.Errors.Select(e => e.Description))}" };
            }

            // Ensure SuperAdmin role exists in the new tenant database
            if (!await _roleManager.RoleExistsAsync(AppRoles.SuperAdmin))
            {
                await _roleManager.CreateAsync(new IdentityRole(AppRoles.SuperAdmin));
            }

            await _userManager.AddToRoleAsync(adminUser, AppRoles.SuperAdmin);

            // 6. Commit
            await transaction.CommitAsync();

            return new TenantOnboardingResult
            {
                Success = true,
                Message = "Tenant onboarded successfully.",
                TenantGuid = tenantGuid,
                AdminEmail = adminEmail,
                TemporaryPassword = tempPassword
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to onboard new tenant {Slug}", request.Slug);
            return new TenantOnboardingResult { Success = false, Message = $"Internal error during onboarding: {ex.Message}" };
        }
    }

    public async Task<PagedResponseDto<TenantListDto>> GetAllTenantsAsync(TenantQueryDto query)
    {
        var dbQuery = _masterContext.Tenants.AsQueryable();

        // Status Filter
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (Enum.TryParse<TenantStatus>(query.Status, true, out var statusEnum))
            {
                dbQuery = dbQuery.Where(t => t.Status == statusEnum);
            }
        }

        // Search Filter
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.ToLower();
            dbQuery = dbQuery.Where(t => t.Name.ToLower().Contains(s) || 
                                         t.Slug.ToLower().Contains(s) || 
                                         t.Subdomain.ToLower().Contains(s));
        }

        var total = await dbQuery.CountAsync();

        // Sort By
        dbQuery = query.SortBy?.ToLower() switch
        {
            "name" => query.SortDirection?.ToLower() == "asc" ? dbQuery.OrderBy(t => t.Name) : dbQuery.OrderByDescending(t => t.Name),
            "status" => query.SortDirection?.ToLower() == "asc" ? dbQuery.OrderBy(t => t.Status) : dbQuery.OrderByDescending(t => t.Status),
            "oldest" => dbQuery.OrderBy(t => t.CreatedAt),
            _ => dbQuery.OrderByDescending(t => t.CreatedAt) // newest by default
        };

        var pagedTenants = await dbQuery
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(t => new
            {
                t.TenantGuid,
                t.Name,
                t.Slug,
                t.Subdomain,
                t.Status,
                t.CreatedAt,
                t.IsLocked,
                Subscription = _masterContext.TenantSubscriptions
                    .Where(s => s.TenantGuid == t.TenantGuid && s.IsActive)
                    .OrderByDescending(s => s.StartsAt)
                    .Select(s => new
                    {
                        PlanId = s.PlanId,
                        PlanName = s.Plan != null ? s.Plan.Name : null,
                        s.IsTrial
                    })
                    .FirstOrDefault()
            })
            .ToListAsync();

        // In-memory filter for PlanId and IsTrial since they are on the Subscription navigation property
        var resultList = pagedTenants.AsEnumerable();

        if (query.PlanId.HasValue)
        {
            resultList = resultList.Where(t => t.Subscription?.PlanId == query.PlanId.Value);
        }

        if (query.IsTrial.HasValue)
        {
            resultList = resultList.Where(t => (t.Subscription?.IsTrial ?? false) == query.IsTrial.Value);
        }

        var result = resultList.Select(t => new TenantListDto
        {
            TenantGuid = t.TenantGuid,
            Name = t.Name,
            Slug = t.Slug,
            Subdomain = t.Subdomain,
            Status = t.Status,
            CreatedAt = t.CreatedAt,
            IsLocked = t.IsLocked,
            PlanName = t.Subscription?.PlanName,
            IsTrial = t.Subscription?.IsTrial ?? false
        }).ToList();

        return new PagedResponseDto<TenantListDto>
        {
            TotalCount = total,
            Page = query.Page,
            PageSize = query.PageSize,
            Items = result
        };
    }

    public async Task<TenantDetailsDto?> GetTenantByIdAsync(Guid tenantGuid)
    {
        var t = await _masterContext.Tenants.FirstOrDefaultAsync(x => x.TenantGuid == tenantGuid);
        if (t == null) return null;

        var sub = await _masterContext.TenantSubscriptions
            .Where(s => s.TenantGuid == tenantGuid && s.IsActive)
            .OrderByDescending(s => s.StartsAt)
            .Select(s => new
            {
                PlanName = s.Plan != null ? s.Plan.Name : null,
                s.ExpiresAt,
                s.IsTrial
            })
            .FirstOrDefaultAsync();

        return new TenantDetailsDto
        {
            TenantGuid = t.TenantGuid,
            Name = t.Name,
            Slug = t.Slug,
            Subdomain = t.Subdomain,
            CustomDomain = t.CustomDomain,
            DatabaseName = t.DatabaseName,
            Status = t.Status,
            IsLocked = t.IsLocked,
            LockedAt = t.LockedAt,
            LockedReason = t.LockedReason,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            PlanName = sub?.PlanName,
            SubscriptionExpiresAt = sub?.ExpiresAt,
            IsTrial = sub?.IsTrial ?? false
        };
    }

    public async Task<(bool Success, string Message)> UpdateTenantAsync(Guid tenantGuid, UpdateTenantDto request)
    {
        var tenant = await _masterContext.Tenants.FirstOrDefaultAsync(x => x.TenantGuid == tenantGuid);
        if (tenant == null)
            return (false, "Tenant not found.");

        if (request.Name != null) tenant.Name = request.Name;
        if (request.Subdomain != null) tenant.Subdomain = request.Subdomain;
        if (request.CustomDomain != null) tenant.CustomDomain = request.CustomDomain;
        if (request.Status.HasValue) tenant.Status = request.Status.Value;

        tenant.UpdatedAt = TimeHelper.GetEgyptTime();

        await _masterContext.SaveChangesAsync();
        InvalidateAnalyticsCache();

        return (true, "Tenant updated successfully.");
    }

    public async Task<TenantUsageDto?> GetTenantUsageAsync(Guid id)
    {
        var cacheKey = $"tenant_usage_{id}";
        if (_cache.TryGetValue(cacheKey, out TenantUsageDto? cachedUsage))
        {
            return cachedUsage!;
        }

        var tenant = await _masterContext.Tenants.FirstOrDefaultAsync(x => x.TenantGuid == id);
        if (tenant == null) return null;

        var usage = new TenantUsageDto();

        var activeSub = await _masterContext.TenantSubscriptions
            .Include(s => s.Plan)
            .Where(s => s.TenantGuid == id && s.IsActive)
            .OrderByDescending(s => s.StartsAt)
            .FirstOrDefaultAsync();

        if (activeSub?.Plan != null)
        {
            usage.PlanLimitUsers = activeSub.Plan.MaxUsers;
            usage.PlanLimitBranches = activeSub.Plan.MaxBranches;
            usage.PlanLimitStorageBytes = activeSub.Plan.MaxStorageGB * 1024L * 1024L * 1024L;
        }

        var tenantUsage = await _masterContext.TenantUsages.FirstOrDefaultAsync(u => u.TenantGuid == id);
        if (tenantUsage != null)
        {
            usage.StorageUsedBytes = tenantUsage.StorageUsedBytes;
        }

        try
        {
            _tenantContext.SetTenant(tenant);
            using var appContext = await _dbContextFactory.CreateDbContextAsync();
            
            if (await appContext.Database.CanConnectAsync())
            {
                usage.UsersCount = await appContext.Users.CountAsync();
                usage.BranchesCount = await appContext.Branches.CountAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch AppDbContext usage for tenant {TenantGuid}", id);
        }

        _cache.Set(cacheKey, usage, TimeSpan.FromMinutes(5));

        return usage;
    }

    public async Task<(bool Success, string Message)> LockTenantAsync(Guid id, string? reason)
    {
        var tenant = await _masterContext.Tenants.FirstOrDefaultAsync(x => x.TenantGuid == id);
        if (tenant == null)
            return (false, "Tenant not found.");

        if (tenant.IsLocked)
            return (false, "Tenant is already locked.");

        tenant.IsLocked = true;
        tenant.LockedAt = TimeHelper.GetEgyptTime();
        tenant.LockedReason = reason;

        await _masterContext.SaveChangesAsync();
        
        _logger.LogInformation("Tenant {Slug} has been locked. Reason: {Reason}", tenant.Slug, reason);
        InvalidateAnalyticsCache();
        
        return (true, $"Tenant '{tenant.Name}' has been locked successfully.");
    }

    public async Task<(bool Success, string Message)> UnlockTenantAsync(Guid id)
    {
        var tenant = await _masterContext.Tenants.FirstOrDefaultAsync(x => x.TenantGuid == id);
        if (tenant == null)
            return (false, "Tenant not found.");

        if (!tenant.IsLocked)
            return (false, "Tenant is not locked.");

        tenant.IsLocked = false;
        tenant.LockedAt = null;
        tenant.LockedReason = null;

        await _masterContext.SaveChangesAsync();
        
        _logger.LogInformation("Tenant {Slug} has been unlocked.", tenant.Slug);
        InvalidateAnalyticsCache();
        
        return (true, $"Tenant '{tenant.Name}' has been unlocked successfully.");
    }

    private void InvalidateAnalyticsCache()
    {
        _cache.Remove("analytics_dashboard");
    }
}
