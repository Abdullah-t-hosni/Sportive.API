using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Hubs;
using Sportive.API.Models;
using Sportive.API.Utils;
using Hangfire;

namespace Sportive.API.Services
{
    public interface IStatisticsService
    {
        Task UpdateDailyStatsAsync(DateTime date);
        Task BackfillStatsAsync(DateTime start, DateTime end);
    }

    [Queue("stats")]
    public class StatisticsService : IStatisticsService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<StatisticsService> _logger;
        private readonly IHubContext<NotificationHub> _hub;
        private readonly ICacheService _cache;
        private readonly Sportive.API.Interfaces.ITenantContext _tenantContext;

        public StatisticsService(AppDbContext db, ILogger<StatisticsService> logger, IHubContext<NotificationHub> hub, ICacheService cache, Sportive.API.Interfaces.ITenantContext tenantContext)
        {
            _db = db;
            _logger = logger;
            _hub = hub;
            _cache = cache;
            _tenantContext = tenantContext;
        }

        public async Task UpdateDailyStatsAsync(DateTime date)
        {
            var start = date.Date;
            var end = start.AddDays(1);

            // Fetch all data for the day once
            var dayOrders = await _db.Orders.AsNoTracking()
                .Where(o => o.CreatedAt >= start && o.CreatedAt < end && o.Status != OrderStatus.Cancelled)
                .Select(o => new { o.TotalAmount, o.Source })
                .ToListAsync();

            var dayCollections = await _db.ReceiptVouchers.AsNoTracking()
                .Where(v => v.VoucherDate >= start && v.VoucherDate < end)
                .Select(v => new { v.Amount, v.CostCenter })
                .ToListAsync();

            var dayExpenses = await _db.PaymentVouchers.AsNoTracking()
                .Where(v => v.VoucherDate >= start && v.VoucherDate < end)
                .Select(v => new { v.Amount, v.CostCenter })
                .ToListAsync();

            var sources = Enum.GetValues<OrderSource>();

            foreach (var source in sources)
            {
                var stat = await _db.DailyStats.FirstOrDefaultAsync(s => s.Date == start && s.Source == source);
                if (stat == null)
                {
                    stat = new DailyStat { Date = start, Source = source };
                    _db.DailyStats.Add(stat);
                }

                // Filtering logic
                var filteredOrders = source == OrderSource.General ? dayOrders : dayOrders.Where(o => o.Source == source).ToList();
                var filteredCollections = source == OrderSource.General ? dayCollections : dayCollections.Where(v => v.CostCenter == source).ToList();
                var filteredExpenses = source == OrderSource.General ? dayExpenses : dayExpenses.Where(v => v.CostCenter == source).ToList();

                stat.TotalSales = filteredOrders.Sum(o => o.TotalAmount);
                stat.OrdersCount = filteredOrders.Count;
                stat.TotalCollections = filteredCollections.Sum(v => v.Amount);
                stat.TotalExpenses = filteredExpenses.Sum(v => v.Amount);
                stat.Profit = stat.TotalSales - stat.TotalExpenses;
                stat.UpdatedAt = TimeHelper.GetEgyptTime();
            }

            await _db.SaveChangesAsync();
            
            // ⚡ Real-Time: Broadcast to Dashboard and clear cache
            await _cache.RemoveByPrefixAsync("KPI_DASHBOARD");
            var prefix = _tenantContext.CurrentTenant?.Slug?.ToLowerInvariant() ?? "global";
            await _hub.Clients.Group($"{prefix}_Admin").SendAsync("DashboardUpdated", new { date = start });

            _logger.LogInformation("Updated source-specific daily stats for {Date} and notified clients.", start.ToShortDateString());
        }

        public async Task BackfillStatsAsync(DateTime start, DateTime end)
        {
            for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
            {
                await UpdateDailyStatsAsync(d);
            }
        }
    }
}
