using Sportive.API.Models;
using Sportive.API.Data;
using Sportive.API.Utils;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sportive.API.Services
{
    public interface IDashboardEventService
    {
        void NotifyTransactionOccurred(DateTime date);
        void TriggerImmediateProcessing();
    }

    public class DashboardEventService : IDashboardEventService
    {
        private readonly AppDbContext _db;
        private readonly ITenantProvider _tenantProvider;
        private readonly ICacheService _cache;
        private readonly ILogger<DashboardEventService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public DashboardEventService(AppDbContext db, ITenantProvider tenantProvider, ICacheService cache, ILogger<DashboardEventService> logger, IServiceScopeFactory scopeFactory)
        {
            _db = db;
            _tenantProvider = tenantProvider;
            _cache = cache;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public void NotifyTransactionOccurred(DateTime date)
        {
            var msg = new OutboxMessage
            {
                TenantId = _tenantProvider.GetTenantId(),
                EventType = "StatsUpdate",
                Payload = JsonSerializer.Serialize(new { Date = date }),
                CreatedAt = TimeHelper.GetEgyptTime()
            };
            
            _db.OutboxMessages.Add(msg);
            _logger.LogDebug("Saved OutboxMessage for Tenant {TenantId} on {Date}", msg.TenantId, date.ToShortDateString());
        }

        public void TriggerImmediateProcessing()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // ⚡ Debouncing Trigger: Avoid "Trigger Storms" under high load (e.g. POS)
                    var debounceKey = "DASHBOARD_OUTBOX_TRIGGER_LOCK";
                    
                    var isProcessing = await _cache.GetAsync<string>(debounceKey);
                    if (isProcessing != null)
                    {
                        return; // already triggered recently, skip/debounce
                    }

                    // Set debounce lock for 2 seconds
                    await _cache.SetAsync(debounceKey, "locked", TimeSpan.FromSeconds(2));

                    using var scope = _scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
                    await processor.ProcessMessagesAsync();
                    
                    _logger.LogDebug("Triggered immediate outbox processing (debounced via Task.Run)");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background outbox processing failed.");
                }
            });
        }
    }
}
