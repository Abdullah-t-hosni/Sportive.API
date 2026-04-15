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

            var adminEmail    = Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? "admin@sportive.com";
            var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? "Admin@123456";

            var admin = await userManager.FindByEmailAsync(adminEmail);
            if (admin == null)
            {
                admin = new Sportive.API.Models.AppUser
                {
                    UserName = adminEmail, Email = adminEmail,
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
