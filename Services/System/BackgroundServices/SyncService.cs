using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Interfaces;
using Sportive.API.Services;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services.BackgroundServices;

public class StartupSyncService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<StartupSyncService> _logger;

    public StartupSyncService(IServiceProvider services, ILogger<StartupSyncService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for server to start listening
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        if (stoppingToken.IsCancellationRequested) return;

        try
        {
            using var masterScope = _services.CreateScope();
            var masterDb = masterScope.ServiceProvider.GetRequiredService<Sportive.API.Data.MasterDbContext>();
            var tenants = await masterDb.Tenants.Where(t => t.Status == Sportive.API.Models.TenantStatus.Active).ToListAsync(stoppingToken);

            _logger.LogInformation("[StartupSync] Periodic accounting and stock synchronization started for {Count} tenants...", tenants.Count);

            foreach (var tenant in tenants)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    _logger.LogInformation("[StartupSync] Processing tenant: {TenantName} ({TenantSlug})", tenant.Name, tenant.Slug);
                    using var scope = _services.CreateScope();
                    var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                    tenantContext.SetTenant(tenant);

                    // ── DATABASE MIGRATION & SEEDING ──────────────────
                    var db          = scope.ServiceProvider.GetRequiredService<Sportive.API.Data.AppDbContext>();
            var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Sportive.API.Models.AppUser>>();

            _logger.LogInformation("[StartupSync] Running migrations...");
            try { await db.Database.MigrateAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "[StartupSync] Migration failed, continuing..."); }

            foreach (var role in AppRoles.All)
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole(role));

            // ── DEFAULT SIZE GROUPS ──────────────────────────
            var defaultGroups = new List<SizeGroup>
            {
                new SizeGroup {
                    Name = "أحذية (الأوروبية)", Description = "المقاسات الأوروبية للأحذية",
                    Values = new List<SizeValue> {
                        new SizeValue { Value = "36", SortOrder = 1 }, new SizeValue { Value = "37", SortOrder = 2 },
                        new SizeValue { Value = "38", SortOrder = 3 }, new SizeValue { Value = "39", SortOrder = 4 },
                        new SizeValue { Value = "40", SortOrder = 5 }, new SizeValue { Value = "41", SortOrder = 6 },
                        new SizeValue { Value = "42", SortOrder = 7 }, new SizeValue { Value = "43", SortOrder = 8 },
                        new SizeValue { Value = "44", SortOrder = 9 }, new SizeValue { Value = "45", SortOrder = 10 },
                        new SizeValue { Value = "46", SortOrder = 11 }
                    }
                },
                new SizeGroup {
                    Name = "ملابس رياضية (أحرف)", Description = "المقاسات العالمية للملابس (S-XXL)",
                    Values = new List<SizeValue> {
                        new SizeValue { Value = "XS", SortOrder = 1 }, new SizeValue { Value = "S", SortOrder = 2 },
                        new SizeValue { Value = "M", SortOrder = 3 }, new SizeValue { Value = "L", SortOrder = 4 },
                        new SizeValue { Value = "XL", SortOrder = 5 }, new SizeValue { Value = "XXL", SortOrder = 6 },
                        new SizeValue { Value = "3XL", SortOrder = 7 }
                    }
                },
                new SizeGroup {
                    Name = "ملابس أطفال (بالأعمار)", Description = "المقاسات المعتمدة على السن",
                    Values = new List<SizeValue> {
                        new SizeValue { Value = "2Y", SortOrder = 1 }, new SizeValue { Value = "4Y", SortOrder = 2 },
                        new SizeValue { Value = "6Y", SortOrder = 3 }, new SizeValue { Value = "8Y", SortOrder = 4 },
                        new SizeValue { Value = "10Y", SortOrder = 5 }, new SizeValue { Value = "12Y", SortOrder = 6 },
                        new SizeValue { Value = "14Y", SortOrder = 7 }
                    }
                },
                new SizeGroup {
                    Name = "أدوات ومعدات", Description = "للأدوات الرياضية التي لا تتطلب مقاسات متعددة",
                    Values = new List<SizeValue> {
                        new SizeValue { Value = "مقاس موحد", SortOrder = 1 }
                    }
                }
            };

            bool changed = false;
            foreach (var group in defaultGroups)
            {
                if (!await db.SizeGroups.AnyAsync(g => g.Name == group.Name, stoppingToken))
                {
                    await db.SizeGroups.AddAsync(group, stoppingToken);
                    changed = true;
                    _logger.LogInformation("[StartupSync] Seeding missing Size Group: {GroupName}", group.Name);
                }
            }
            if (changed) await db.SaveChangesAsync(stoppingToken);

            // ── DEFAULT BRANCHES ──────────────────────────────
            if (!await db.Branches.AnyAsync(b => b.Name == "الموقع الإلكتروني", stoppingToken))
            {
                await db.Branches.AddAsync(new Branch { Name = "الموقع الإلكتروني", Address = "الويب سايت الرئيسي", PhoneNumber = "-", IsActive = true }, stoppingToken);
                await db.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("[StartupSync] Seeded Website Branch.");
            }

            var adminEmail    = Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? "admin@sportive.com";
            var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? "Admin@123456";

            var admin = await userManager.FindByEmailAsync(adminEmail);
            if (admin == null)
            {
                admin = new Sportive.API.Models.AppUser
                {
                    UserName = "staff_" + adminEmail, Email = adminEmail,
                    PhoneNumber = "01111111111", FullName = "Sport Zone", IsActive = true
                };
                await userManager.CreateAsync(admin, adminPassword);
                await userManager.AddToRoleAsync(admin, AppRoles.SuperAdmin);
                await userManager.AddToRoleAsync(admin, AppRoles.Admin);
                _logger.LogInformation("[StartupSync] Default super admin user created.");
            }

            // ── EXISTING HEAVY SYNCS ──────────────────────────
            var customerService   = scope.ServiceProvider.GetRequiredService<ICustomerService>();
            var productService    = scope.ServiceProvider.GetRequiredService<IProductService>();
            var orderService      = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var accountingService = scope.ServiceProvider.GetRequiredService<IAccountingService>();

            await customerService.SyncAllMissingAccountsAsync();
            // ✅ Incremental: only fixes products with wrong status/missing slug (not full table scan)
            await productService.SyncAllProductsStatusAndStockAsync();
            // ⛔ Removed from startup: SyncAllProductRatingsAsync() loads ALL products+reviews into RAM.
            //    Ratings are already kept in sync atomically in ReviewService on every approval.
            //    Call manually from admin dashboard if a repair is needed.
            
            // ⛔ Removed from startup to prevent database connection starvation and startup hangs:
            // await orderService.SyncAllOrderAccountingAsync(daysLimit: 30);
            // await accountingService.SyncAllPurchaseAccountingAsync(daysLimit: 30);

            // ── DASHBOARD PRE-AGGREGATION BACKFILL ────────────
            var statsService = scope.ServiceProvider.GetRequiredService<IStatisticsService>();
            if (!await db.DailyStats.AnyAsync(stoppingToken))
            {
                _logger.LogInformation("[StartupSync] DailyStats table is empty. Running backfill for the last 90 days...");
                await statsService.BackfillStatsAsync(TimeHelper.GetEgyptTime().Date.AddDays(-90), TimeHelper.GetEgyptTime().Date);
            }

            _logger.LogInformation("[StartupSync] Completed synchronization for tenant: {TenantName}", tenant.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[StartupSync] Error processing tenant {TenantSlug}", tenant.Slug);
                }
            }

            _logger.LogInformation("[StartupSync] All synchronizations completed successfully for all tenants.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[StartupSync] Synchronization task was cancelled during app shutdown.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StartupSync] Critical error during background synchronization");
        }
    }
}
