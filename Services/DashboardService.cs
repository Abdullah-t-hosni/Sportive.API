using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Hubs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using System.Text;

namespace Sportive.API.Services;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;

    public DashboardService(AppDbContext db, IHubContext<NotificationHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task<DashboardStatsDto> GetStatsAsync()
    {
        var now        = DateTime.UtcNow;
        var todayStart = now.Date;
        var todayEnd   = todayStart.AddDays(1);
        var yesterdayStart = todayStart.AddDays(-1);
        
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var lastMonthStart = monthStart.AddMonths(-1);

        // --- Current Stats ---
        var todaySales = await _db.Orders
            .Where(o => o.CreatedAt >= todayStart && o.CreatedAt < todayEnd && o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var monthSales = await _db.Orders
            .Where(o => o.CreatedAt >= monthStart && o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var totalOrders = await _db.Orders.CountAsync(o => o.Status != OrderStatus.Cancelled);
        var totalCustomers = await _db.Customers.CountAsync(c => !c.IsDeleted);

        // --- Growth Calculation (Previous Periods) ---
        var yesterdayStartDate = todayStart.AddDays(-1);
        var yesterdayOrders = await _db.Orders
            .CountAsync(o => o.CreatedAt >= yesterdayStartDate && o.CreatedAt < todayStart && o.Status != OrderStatus.Cancelled);

        var lastMonthStartMonth = monthStart.AddMonths(-1);
        var prevMonthOrders = await _db.Orders
            .CountAsync(o => o.CreatedAt >= lastMonthStartMonth && o.CreatedAt < monthStart && o.Status != OrderStatus.Cancelled);

        var yesterdaySales = await _db.Orders
            .Where(o => o.CreatedAt >= yesterdayStartDate && o.CreatedAt < todayStart && o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var lastMonthSales = await _db.Orders
            .Where(o => o.CreatedAt >= lastMonthStartMonth && o.CreatedAt < monthStart && o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var prevCustomersCount = await _db.Customers
            .CountAsync(c => c.CreatedAt < monthStart && !c.IsDeleted);

        // Growth Rates
        decimal CalculateGrowth(decimal current, decimal previous) => 
            previous == 0 ? (current > 0 ? 100 : 0) : Math.Round(((current - previous) / previous) * 100, 1);

        var todayGrowth = CalculateGrowth(todaySales, yesterdaySales);
        var monthSalesGrowth = CalculateGrowth(monthSales, lastMonthSales);
        var orderGrowth = CalculateGrowth(totalOrders, prevMonthOrders); // Comparing current total to last month's start total (Simplified but better than 0)
        var customerGrowth = CalculateGrowth(totalCustomers, prevCustomersCount);

        return new DashboardStatsDto(
            TodaySales: todaySales,
            TodaySalesGrowth: todayGrowth,
            ThisMonthSales: monthSales,
            ThisMonthSalesGrowth: monthSalesGrowth,
            TotalRevenue: await _db.Orders.Where(o => o.Status != OrderStatus.Cancelled).SumAsync(o => (decimal?)o.TotalAmount) ?? 0,
            TotalOrders: totalOrders,
            TotalOrdersGrowth: (int)orderGrowth,
            PendingOrders: await _db.Orders.CountAsync(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Confirmed),
            TodayOrders: await _db.Orders.CountAsync(o => o.CreatedAt >= todayStart && o.CreatedAt < todayEnd && o.Status != OrderStatus.Cancelled),
            TotalCustomers: totalCustomers,
            TotalCustomersGrowth: (int)customerGrowth,
            TotalProducts: await _db.Products.CountAsync(p => !p.IsDeleted),
            LowStockProducts: await _db.ProductVariants.CountAsync(v => !v.IsDeleted && v.StockQuantity <= 5 && v.StockQuantity > 0),
            OutOfStockProducts: await _db.ProductVariants.CountAsync(v => !v.IsDeleted && v.StockQuantity == 0)
        );
    }

    public async Task<AnalyticsSummaryDto> GetAnalyticsSummaryAsync()
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1);

        // Category Sales
        var catSales = await _db.OrderItems
            .Include(i => i.Product).ThenInclude(p => p.Category)
            .Where(i => !i.IsDeleted)
            .GroupBy(i => new { i.Product.Category.Id, i.Product.Category.NameAr, i.Product.Category.NameEn })
            .Select(g => new CategorySalesDto(
                g.Key.Id, g.Key.NameAr, g.Key.NameEn,
                g.Sum(i => i.Quantity),
                g.Sum(i => i.TotalPrice)
            ))
            .ToListAsync();

        // Daily Sales & New Customers for last 30 days
        var startDate = now.AddDays(-30);
        var orders = await _db.Orders
            .Where(o => o.CreatedAt >= startDate && o.Status != OrderStatus.Cancelled)
            .Select(o => new { o.CreatedAt, o.TotalAmount })
            .ToListAsync();

        var newCustomers = await _db.Customers
            .Where(c => c.CreatedAt >= startDate && !c.IsDeleted)
            .Select(c => c.CreatedAt)
            .ToListAsync();

        var dailyMetrics = Enumerable.Range(0, 31)
            .Select(offset => startDate.Date.AddDays(offset))
            .Select(date => new DailyMetricDto(
                date,
                orders.Where(o => o.CreatedAt.Date == date).Sum(o => o.TotalAmount),
                orders.Count(o => o.CreatedAt.Date == date),
                newCustomers.Count(c => c.Date == date)
            ))
            .OrderBy(x => x.Date)
            .ToList();

        var totalRev = orders.Sum(o => o.TotalAmount);
        var totalOrd = orders.Count;

        // Retention Rate calculation: (Customers with > 1 order / Total customers with at least 1 order)
        var orderCountsByCustomer = await _db.Orders
            .Where(o => o.Status != OrderStatus.Cancelled)
            .GroupBy(o => o.CustomerId)
            .Select(g => g.Count())
            .ToListAsync();

        decimal retentionRate = 0;
        if (orderCountsByCustomer.Any())
        {
            var repeatCustomers = orderCountsByCustomer.Count(c => c > 1);
            retentionRate = Math.Round(((decimal)repeatCustomers / orderCountsByCustomer.Count) * 100, 1);
        }

        return new AnalyticsSummaryDto(
            CategorySales: catSales,
            TopSellingProducts: await GetTopProductsAsync(5),
            DailySales: dailyMetrics,
            AverageOrderValue: totalOrd > 0 ? Math.Round(totalRev / totalOrd, 1) : 0,
            CustomerRetentionRate: retentionRate
        );
    }

    public async Task<byte[]> ExportSalesToCsvAsync(DateTime? from, DateTime? to)
    {
        var query = _db.Orders.Include(o => o.Customer).Where(o => !o.IsDeleted);
        if (from.HasValue) query = query.Where(o => o.CreatedAt >= from.Value);
        if (to.HasValue)   query = query.Where(o => o.CreatedAt <= to.Value);

        var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
        
        var csv = new StringBuilder();
        csv.AppendLine("OrderNumber,Date,Customer,Email,Status,TotalAmount");
        foreach(var o in orders)
        {
            csv.AppendLine($"{o.OrderNumber},{o.CreatedAt:yyyy-MM-dd HH:mm},{o.Customer.FirstName} {o.Customer.LastName},{o.Customer.Email},{o.Status},{o.TotalAmount}");
        }
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    public async Task TriggerLiveUpdateAsync()
    {
        // Pushes a refresh event to all clients in the "Admin" group
        await _hub.Clients.Group("Admin").SendAsync("RefreshDashboard");
    }

    // --- Original Methods (Modified slightly for consistency) ---

    public async Task<List<SalesChartDto>> GetSalesChartAsync(string period)
    {
        var now = DateTime.UtcNow;
        if (period == "daily")
        {
            var from = now.AddDays(-29).Date;
            var orders = await _db.Orders.Where(o => o.CreatedAt >= from && o.Status != OrderStatus.Cancelled).Select(o => new { o.CreatedAt, o.TotalAmount }).ToListAsync();
            return orders.GroupBy(o => o.CreatedAt.Date).Select(g => new SalesChartDto(g.Key.ToString("MM/dd"), g.Sum(o => o.TotalAmount), g.Count())).OrderBy(x => x.Label).ToList();
        }
        else // monthly
        {
            var from = new DateTime(now.Year - 1, now.Month, 1);
            var orders = await _db.Orders.Where(o => o.CreatedAt >= from && o.Status != OrderStatus.Cancelled).Select(o => new { o.CreatedAt, o.TotalAmount }).ToListAsync();
            return orders.GroupBy(o => new { o.CreatedAt.Year, o.CreatedAt.Month }).Select(g => new SalesChartDto($"{g.Key.Month}/{g.Key.Year}", g.Sum(o => o.TotalAmount), g.Count())).OrderBy(x => x.Label).ToList();
        }
    }

    public async Task<List<TopProductDto>> GetTopProductsAsync(int count = 10)
    {
        var items = await _db.OrderItems.Include(i => i.Product).ThenInclude(p => p.Images)
            .Where(i => !i.IsDeleted)
            .Select(i => new { i.ProductId, i.ProductNameAr, i.ProductNameEn, i.Quantity, i.TotalPrice, MainImage = i.Product.Images.Where(img => img.IsMain && !img.IsDeleted).Select(img => img.ImageUrl).FirstOrDefault() })
            .ToListAsync();

        return items.GroupBy(i => i.ProductId).Select(g => new TopProductDto(g.Key, g.First().ProductNameAr, g.First().ProductNameEn, g.First().MainImage, g.Sum(i => i.Quantity), g.Sum(i => i.TotalPrice))).OrderByDescending(x => x.TotalSold).Take(count).ToList();
    }

    public async Task<List<OrderStatusStatsDto>> GetOrderStatusStatsAsync()
    {
        var total = await _db.Orders.CountAsync(o => !o.IsDeleted);
        if (total == 0) return new List<OrderStatusStatsDto>();
        var groups = await _db.Orders.Where(o => !o.IsDeleted).GroupBy(o => o.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        return groups.Select(g => new OrderStatusStatsDto(g.Status.ToString(), g.Count, Math.Round((decimal)g.Count / total * 100, 1))).ToList();
    }

    public async Task<List<OrderSummaryDto>> GetRecentOrdersAsync(int count = 10)
    {
        return await _db.Orders.Include(o => o.Customer).Include(o => o.Items).Where(o => !o.IsDeleted).OrderByDescending(o => o.CreatedAt).Take(count)
            .Select(o => new OrderSummaryDto(o.Id, o.OrderNumber, o.Customer.FirstName + " " + o.Customer.LastName, o.Customer.Phone ?? "", o.Status.ToString(), o.FulfillmentType.ToString(), o.TotalAmount, o.CreatedAt, o.Items.Sum(i => i.Quantity)))
            .ToListAsync();
    }
}
