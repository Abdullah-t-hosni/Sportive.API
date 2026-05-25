using Sportive.API.Models;
using Sportive.API.Data;
using Sportive.API.Utils;
using System.Text.Json;
using Hangfire;

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
        private readonly IBackgroundJobClient _backgroundJobs;
        private readonly ICacheService _cache;
        private readonly ILogger<DashboardEventService> _logger;

        public DashboardEventService(AppDbContext db, ITenantProvider tenantProvider, IBackgroundJobClient backgroundJobs, ICacheService cache, ILogger<DashboardEventService> logger)
        {
            _db = db;
            _tenantProvider = tenantProvider;
            _backgroundJobs = backgroundJobs;
            _cache = cache;
            _logger = logger;
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
            // ⚡ Debouncing Trigger: Avoid "Trigger Storms" under high load (e.g. POS)
            var debounceKey = "DASHBOARD_OUTBOX_TRIGGER_LOCK";
            
            // Only trigger if not already triggered in the last 2 seconds
            if (_cache.GetOrCreateAsync(debounceKey, () => Task.FromResult(true), TimeSpan.FromSeconds(2)).Result)
            {
                _backgroundJobs.Enqueue<IOutboxProcessor>(p => p.ProcessMessagesAsync());
                _logger.LogDebug("Triggered immediate outbox processing (debounced)");
            }
        }
    }
}
