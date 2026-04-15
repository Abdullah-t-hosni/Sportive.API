using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sportive.API.Interfaces;
using Sportive.API.Services;

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

            var customerService   = scope.ServiceProvider.GetRequiredService<ICustomerService>();
            var productService    = scope.ServiceProvider.GetRequiredService<IProductService>();
            var orderService      = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var accountingService = scope.ServiceProvider.GetRequiredService<IAccountingService>();

            _logger.LogInformation("[StartupSync] Periodic accounting and stock synchronization started...");

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
