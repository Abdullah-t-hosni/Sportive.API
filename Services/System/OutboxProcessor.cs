using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services
{
    public interface IOutboxProcessor
    {
        Task ProcessMessagesAsync();
    }

    public class OutboxProcessor : IOutboxProcessor
    {
        private readonly AppDbContext _db;
        private readonly IStatisticsService _statsService;
        private readonly ILogger<OutboxProcessor> _logger;

        public OutboxProcessor(AppDbContext db, IStatisticsService statsService, ILogger<OutboxProcessor> logger)
        {
            _db = db;
            _statsService = statsService;
            _logger = logger;
        }

        public async Task ProcessMessagesAsync()
        {
            // ⚡ Optimization: Process in batches of 100 for high-traffic scalability
            var messages = await _db.OutboxMessages
                .Where(m => m.ProcessedAt == null && m.RetryCount < 5)
                .OrderBy(m => m.CreatedAt)
                .Take(100)
                .ToListAsync();

            if (!messages.Any()) return;

            foreach (var msg in messages)
            {
                // ⚡ Double-Check Idempotency: Ensure no race condition processed this while we were fetching
                var currentMsg = await _db.OutboxMessages.FindAsync(msg.Id);
                if (currentMsg == null || currentMsg.ProcessedAt != null) continue;

                try
                {
                    if (msg.EventType == "StatsUpdate")
                    {
                        var data = JsonSerializer.Deserialize<StatsUpdatePayload>(msg.Payload);
                        if (data != null)
                        {
                            await _statsService.UpdateDailyStatsAsync(data.Date);
                        }
                    }

                    msg.ProcessedAt = TimeHelper.GetEgyptTime();
                    _logger.LogInformation("Processed outbox message {Id} [{Type}] for Tenant {TenantId}", msg.Id, msg.EventType, msg.TenantId);
                }
                catch (Exception ex)
                {
                    msg.RetryCount++;
                    msg.Error = ex.Message;
                    _logger.LogError(ex, "Failed to process outbox message {Id}", msg.Id);
                }
            }

            await _db.SaveChangesAsync();

            // ⚡ Housekeeping: Periodically remove old processed messages (older than 24h)
            var cutoff = TimeHelper.GetEgyptTime().AddDays(-1);
            var staleMessages = await _db.OutboxMessages
                .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
                .Take(500)
                .ToListAsync();
            
            if (staleMessages.Any())
            {
                _db.OutboxMessages.RemoveRange(staleMessages);
                await _db.SaveChangesAsync();
            }
        }

        private class StatsUpdatePayload
        {
            public DateTime Date { get; set; }
        }
    }
}
