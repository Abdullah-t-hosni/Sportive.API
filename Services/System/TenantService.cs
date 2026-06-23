using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services;

public interface ITenantService
{
    Task<TenantOnboardingResult> OnboardNewTenantAsync(OnboardTenantRequest request);
    Task<object> GetAllTenantsAsync(int page = 1, int pageSize = 20, string? search = null);
    Task<TenantListDto?> GetTenantByIdAsync(Guid id);
    Task<TenantUsageDto?> GetTenantUsageAsync(Guid id);
    Task<SuperAdminDashboardStatsDto> GetDashboardStatsAsync();
    Task<(bool Success, string Message)> LockTenantAsync(Guid id);
    Task<(bool Success, string Message)> UnlockTenantAsync(Guid id);
}

public class TenantService : ITenantService
{
    private readonly MasterDbContext _masterContext;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ITenantContext _tenantContext;
    private readonly UserManager<AppUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ILogger<TenantService> _logger;

    public TenantService(
        MasterDbContext masterContext,
        IDbContextFactory<AppDbContext> dbContextFactory,
        ITenantContext tenantContext,
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ILogger<TenantService> logger)
    {
        _masterContext = masterContext;
        _dbContextFactory = dbContextFactory;
        _tenantContext = tenantContext;
        _userManager = userManager;
        _roleManager = roleManager;
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

    public async Task<object> GetAllTenantsAsync(int page = 1, int pageSize = 20, string? search = null)
    {
        var query = _masterContext.Tenants.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(x => x.Name.ToLower().Contains(s) || x.Slug.ToLower().Contains(s) || x.Subdomain.ToLower().Contains(s));
        }

        var total = await query.CountAsync();
        
        var tenants = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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
                        PlanName = _masterContext.Plans.Where(p => p.Id == s.PlanId).Select(p => p.Name).FirstOrDefault(),
                        s.ExpiresAt,
                        s.IsTrial
                    })
                    .FirstOrDefault()
            })
            .ToListAsync();

        var result = tenants.Select(t => new TenantListDto
        {
            TenantGuid = t.TenantGuid,
            Name = t.Name,
            Slug = t.Slug,
            Subdomain = t.Subdomain,
            Status = t.Status,
            CreatedAt = t.CreatedAt,
            IsLocked = t.IsLocked,
            CurrentPlanName = t.Subscription?.PlanName,
            SubscriptionExpiresAt = t.Subscription?.ExpiresAt,
            IsTrial = t.Subscription?.IsTrial ?? false
        }).ToList();

        return new { total, page, pageSize, data = result };
    }

    public async Task<TenantListDto?> GetTenantByIdAsync(Guid id)
    {
        var t = await _masterContext.Tenants.FirstOrDefaultAsync(x => x.TenantGuid == id);
        if (t == null) return null;

        var sub = await _masterContext.TenantSubscriptions
            .Where(s => s.TenantGuid == id && s.IsActive)
            .OrderByDescending(s => s.StartsAt)
            .Select(s => new
            {
                PlanName = _masterContext.Plans.Where(p => p.Id == s.PlanId).Select(p => p.Name).FirstOrDefault(),
                s.ExpiresAt,
                s.IsTrial
            })
            .FirstOrDefaultAsync();

        return new TenantListDto
        {
            TenantGuid = t.TenantGuid,
            Name = t.Name,
            Slug = t.Slug,
            Subdomain = t.Subdomain,
            Status = t.Status,
            CreatedAt = t.CreatedAt,
            IsLocked = t.IsLocked,
            CurrentPlanName = sub?.PlanName,
            SubscriptionExpiresAt = sub?.ExpiresAt,
            IsTrial = sub?.IsTrial ?? false
        };
    }

    public async Task<TenantUsageDto?> GetTenantUsageAsync(Guid id)
    {
        var tenant = await _masterContext.Tenants.FirstOrDefaultAsync(x => x.TenantGuid == id);
        if (tenant == null) return null;

        var usage = new TenantUsageDto();

        var activeSub = await _masterContext.TenantSubscriptions
            .Where(s => s.TenantGuid == id && s.IsActive)
            .OrderByDescending(s => s.StartsAt)
            .FirstOrDefaultAsync();

        if (activeSub != null)
        {
            var plan = await _masterContext.Plans.FirstOrDefaultAsync(p => p.Id == activeSub.PlanId);
            if (plan != null)
            {
                usage.PlanLimitUsers = plan.MaxUsers;
                usage.PlanLimitBranches = plan.MaxBranches;
                usage.PlanLimitStorageBytes = plan.MaxStorageGB * 1024L * 1024L * 1024L;
            }
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
            
            // Check if db can connect
            if (await appContext.Database.CanConnectAsync())
            {
                // Usage from app DB
                usage.UsersCount = await appContext.Users.CountAsync();
                usage.BranchesCount = await appContext.Branches.CountAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch AppDbContext usage for tenant {TenantGuid}", id);
        }

        return usage;
    }

    public async Task<SuperAdminDashboardStatsDto> GetDashboardStatsAsync()
    {
        var tenants = await _masterContext.Tenants.ToListAsync();
        var subs = await _masterContext.TenantSubscriptions.Where(s => s.IsActive).ToListAsync();

        var total = tenants.Count;
        var active = tenants.Count(t => t.Status == TenantStatus.Active && !t.IsLocked);
        var locked = tenants.Count(t => t.IsLocked);
        
        var trial = subs.Count(s => s.IsTrial);
        var expired = subs.Count(s => s.ExpiresAt < TimeHelper.GetEgyptTime());

        return new SuperAdminDashboardStatsDto
        {
            TotalTenants = total,
            ActiveTenants = active,
            TrialTenants = trial,
            ExpiredTenants = expired,
            LockedTenants = locked
        };
    }

    public async Task<(bool Success, string Message)> LockTenantAsync(Guid id)
    {
        var tenant = await _masterContext.Tenants.FirstOrDefaultAsync(x => x.TenantGuid == id);
        if (tenant == null)
            return (false, "Tenant not found.");

        if (tenant.IsLocked)
            return (false, "Tenant is already locked.");

        tenant.IsLocked = true;
        await _masterContext.SaveChangesAsync();
        _logger.LogInformation("Tenant {Slug} has been locked.", tenant.Slug);
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
        await _masterContext.SaveChangesAsync();
        _logger.LogInformation("Tenant {Slug} has been unlocked.", tenant.Slug);
        return (true, $"Tenant '{tenant.Name}' has been unlocked successfully.");
    }
}
