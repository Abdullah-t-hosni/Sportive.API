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
}
