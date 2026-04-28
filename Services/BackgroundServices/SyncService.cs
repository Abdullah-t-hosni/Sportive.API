using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Interfaces;
using Sportive.API.Services;
using Sportive.API.Models;

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
            using var scope = _services.CreateScope();

            _logger.LogInformation("[StartupSync] Periodic accounting and stock synchronization started...");

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
            if (!await db.SizeGroups.AnyAsync(stoppingToken))
            {
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
                    }
                };
                await db.SizeGroups.AddRangeAsync(defaultGroups, stoppingToken);
                await db.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("[StartupSync] Seeded default Size Groups.");
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
                await userManager.AddToRoleAsync(admin, "Admin");
                _logger.LogInformation("[StartupSync] Default admin user created.");
            }

            // ── EXISTING HEAVY SYNCS ──────────────────────────
            var customerService   = scope.ServiceProvider.GetRequiredService<ICustomerService>();
            var productService    = scope.ServiceProvider.GetRequiredService<IProductService>();
            var orderService      = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var accountingService = scope.ServiceProvider.GetRequiredService<IAccountingService>();

            await customerService.SyncAllMissingAccountsAsync();
            await productService.SyncAllProductsStatusAndStockAsync();
            await productService.SyncAllProductRatingsAsync();
            await orderService.SyncAllOrderAccountingAsync();
            await accountingService.SyncAllPurchaseAccountingAsync();

            _logger.LogInformation("[StartupSync] All synchronizations completed successfully.");
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
