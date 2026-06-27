using System;
using System.Linq;
using System.Text;
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
    private readonly IServiceProvider _serviceProvider;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TenantService> _logger;
    private readonly IEmailService _emailService;

    public TenantService(
        MasterDbContext masterContext,
        IDbContextFactory<AppDbContext> dbContextFactory,
        ITenantContext tenantContext,
        IServiceProvider serviceProvider,
        IMemoryCache cache,
        ILogger<TenantService> logger,
        IEmailService emailService)
    {
        _masterContext = masterContext;
        _dbContextFactory = dbContextFactory;
        _tenantContext = tenantContext;
        _serviceProvider = serviceProvider;
        _cache = cache;
        _logger = logger;
        _emailService = emailService;
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

        var strategy = _masterContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _masterContext.Database.BeginTransactionAsync();
            try
            {
                // Set context so AppDbContext uses the new database connection string
                _tenantContext.SetTenant(newTenant);

                // 1. Check connection
                using var appContext = await _dbContextFactory.CreateDbContextAsync();
                // 1. Context created successfully

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

                var userManager = _serviceProvider.GetRequiredService<UserManager<AppUser>>();
                var roleManager = _serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

                var userResult = await userManager.CreateAsync(adminUser, tempPassword);
                if (!userResult.Succeeded)
                {
                    await transaction.RollbackAsync();
                    return new TenantOnboardingResult { Success = false, Message = $"Failed to create admin user: {string.Join(", ", userResult.Errors.Select(e => e.Description))}" };
                }

                // Ensure SuperAdmin role exists in the new tenant database
                if (!await roleManager.RoleExistsAsync(AppRoles.SuperAdmin))
                {
                    await roleManager.CreateAsync(new IdentityRole(AppRoles.SuperAdmin));
                }

                await userManager.AddToRoleAsync(adminUser, AppRoles.SuperAdmin);

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
        });
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
                        ExpiresAt = (DateTime?)s.ExpiresAt,
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
            SubscriptionExpiresAt = t.Subscription?.ExpiresAt,
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

        if (request.SubscriptionExpiresAt.HasValue)
        {
            var activeSub = await _masterContext.TenantSubscriptions
                .Where(s => s.TenantGuid == tenantGuid && s.IsActive)
                .OrderByDescending(s => s.StartsAt)
                .FirstOrDefaultAsync();

            if (activeSub != null)
            {
                var dt = request.SubscriptionExpiresAt.Value;
                if (dt.Kind == DateTimeKind.Utc)
                {
                    dt = dt.ToStoreTime();
                }
                activeSub.ExpiresAt = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
                activeSub.UpdatedAt = TimeHelper.GetEgyptTime();
            }
        }

        tenant.UpdatedAt = TimeHelper.GetEgyptTime();

        await _masterContext.SaveChangesAsync();
        InvalidateAnalyticsCache();

        _cache.Remove($"tenant_slug_{tenant.Slug.ToLowerInvariant()}");
        _cache.Remove($"tenant_subdomain_{tenant.Subdomain.ToLowerInvariant()}");
        if (!string.IsNullOrEmpty(tenant.CustomDomain))
        {
            _cache.Remove($"tenant_customdomain_{tenant.CustomDomain.ToLowerInvariant()}");
        }

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

    public async Task<bool> IsSlugAvailableAsync(string slug)
    {
        var normalized = slug.ToLowerInvariant().Trim();
        return !await _masterContext.Tenants.AnyAsync(t => t.Slug == normalized || t.Subdomain == normalized);
    }

    public async Task<SelfRegisterResult> SelfRegisterAsync(SelfRegisterRequest request)
    {
        var slug = request.Slug.ToLowerInvariant().Trim();

        // 1. تحقق من الـ slug
        if (await _masterContext.Tenants.AnyAsync(t => t.Slug == slug || t.Subdomain == slug))
            return new SelfRegisterResult { Success = false, Message = "هذا النطاق الفرعي محجوز مسبقاً. الرجاء اختيار اسم آخر." };

        // 2. بناء بيانات قاعدة البيانات تلقائياً من slug
        var dbName = $"raakiza_{slug}";
        var dbUser = Environment.GetEnvironmentVariable("DEFAULT_TENANT_DB_USER") ?? "root";
        var dbPass = Environment.GetEnvironmentVariable("DEFAULT_TENANT_DB_PASSWORD") ?? "de23dd306edf0ed4";

        // 3. بناء OnboardTenantRequest الكاملة
        var onboardRequest = new OnboardTenantRequest
        {
            Name         = request.CompanyName,
            Slug         = slug,
            Subdomain    = slug,
            DatabaseName = dbName,
            DatabaseUser = dbUser,
            DatabasePassword = dbPass
        };

        // 4. تنفيذ الـ onboarding الأساسي
        var result = await OnboardNewTenantAsync(onboardRequest);

        if (!result.Success)
            return new SelfRegisterResult { Success = false, Message = result.Message };

        // 5. تحديث البريد الإلكتروني للمدير بالـ email الحقيقي للعميل
        // الـ adminEmail الأصلي: admin@{slug}.local
        // سنحتفظ بهذا ونضيف email حقيقي للعميل كـ contact
        var adminEmail = result.AdminEmail ?? $"admin@{slug}.local";
        var tempPassword = result.TemporaryPassword ?? string.Empty;
        var subdomain = $"{slug}.raakiza.com";

        // 6. إرسال إيميل الترحيب للعميل
        try
        {
            await _emailService.SendEmailAsync(
                request.Email,
                $"مرحباً بك في ركيزة — حسابك جاهز! 🎉",
                BuildWelcomeEmailBody(request.ContactName, request.CompanyName, subdomain, adminEmail, tempPassword)
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send welcome email to {Email} for tenant {Slug}", request.Email, slug);
            // لا نفشل العملية بسبب فشل الإيميل
        }

        _logger.LogInformation("Self-registration completed for tenant {Slug} by {Email}", slug, request.Email);

        return new SelfRegisterResult
        {
            Success = true,
            Message = "تم إنشاء حسابك بنجاح! تحقق من بريدك الإلكتروني للحصول على بيانات الدخول.",
            Subdomain = subdomain,
            AdminEmail = adminEmail
        };
    }

    private static string BuildWelcomeEmailBody(string contactName, string companyName, string subdomain, string adminEmail, string tempPassword)
    {
        return $@"
<div dir='rtl' style='font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif; max-width: 600px; margin: 0 auto; background-color: #ffffff;'>
  
  <!-- Header -->
  <div style='background: linear-gradient(135deg, #1D1D1F 0%, #3a3a3c 100%); padding: 40px 32px; text-align: center; border-radius: 16px 16px 0 0;'>
    <h1 style='color: #ffffff; margin: 0; font-size: 28px; font-weight: 800; letter-spacing: -0.5px;'>ركيزة</h1>
    <p style='color: rgba(255,255,255,0.7); margin: 8px 0 0 0; font-size: 14px;'>منصة إدارة الأعمال المتكاملة</p>
  </div>

  <!-- Body -->
  <div style='padding: 40px 32px; border: 1px solid #e5e7eb; border-top: none; border-radius: 0 0 16px 16px;'>
    
    <h2 style='color: #1D1D1F; font-size: 22px; font-weight: 700; margin: 0 0 16px 0;'>مرحباً {contactName}! 🎉</h2>
    <p style='color: #6b7280; font-size: 16px; line-height: 1.6; margin: 0 0 24px 0;'>
      يسعدنا الإعلان عن إطلاق حساب شركة <strong style='color: #1D1D1F;'>{companyName}</strong> على منصة ركيزة بنجاح.
      حسابك جاهز تماماً ويمكنك البدء فوراً!
    </p>

    <!-- Access Box -->
    <div style='background: #f9fafb; border: 1px solid #e5e7eb; border-radius: 12px; padding: 24px; margin: 0 0 32px 0;'>
      <h3 style='color: #1D1D1F; font-size: 14px; font-weight: 700; margin: 0 0 16px 0; text-transform: uppercase; letter-spacing: 0.5px;'>بيانات الدخول</h3>
      
      <div style='margin-bottom: 12px;'>
        <span style='color: #6b7280; font-size: 12px; font-weight: 600; text-transform: uppercase;'>رابط نظامك</span>
        <div style='margin-top: 4px;'>
          <a href='https://{subdomain}' style='color: #0066CC; font-size: 16px; font-weight: 600; text-decoration: none;'>https://{subdomain}</a>
        </div>
      </div>
      
      <div style='margin-bottom: 12px;'>
        <span style='color: #6b7280; font-size: 12px; font-weight: 600; text-transform: uppercase;'>اسم المستخدم (الإيميل)</span>
        <div style='margin-top: 4px; font-family: monospace; font-size: 15px; color: #1D1D1F; font-weight: 600;'>{adminEmail}</div>
      </div>
      
      <div>
        <span style='color: #6b7280; font-size: 12px; font-weight: 600; text-transform: uppercase;'>كلمة المرور المؤقتة</span>
        <div style='margin-top: 4px; background: #1D1D1F; color: #ffffff; font-family: monospace; font-size: 18px; font-weight: 700; padding: 12px 16px; border-radius: 8px; letter-spacing: 1px;'>{tempPassword}</div>
      </div>
    </div>

    <!-- CTA Button -->
    <div style='text-align: center; margin: 0 0 32px 0;'>
      <a href='https://{subdomain}' style='display: inline-block; background: #1D1D1F; color: #ffffff; padding: 16px 36px; border-radius: 12px; font-size: 16px; font-weight: 700; text-decoration: none; letter-spacing: -0.3px;'>
        ابدأ الاستخدام الآن ←
      </a>
    </div>

    <!-- Warning -->
    <div style='background: #fffbeb; border: 1px solid #fde68a; border-radius: 10px; padding: 16px; margin: 0 0 24px 0;'>
      <p style='color: #92400e; font-size: 13px; margin: 0; line-height: 1.5;'>
        ⚠️ <strong>مهم:</strong> يُرجى تغيير كلمة المرور المؤقتة فور تسجيل الدخول للمرة الأولى لضمان أمان حسابك.
      </p>
    </div>

    <p style='color: #9ca3af; font-size: 13px; line-height: 1.6; margin: 0;'>
      إذا واجهتَ أي مشكلة، تواصل معنا على support@raakiza.com وسيسعد فريقنا بمساعدتك في أقرب وقت.
    </p>
  </div>

  <p style='text-align: center; color: #d1d5db; font-size: 11px; margin-top: 20px;'>© 2026 ركيزة — جميع الحقوق محفوظة</p>
</div>";
    }

    private void InvalidateAnalyticsCache()
    {
        _cache.Remove("analytics_dashboard");
    }
}
