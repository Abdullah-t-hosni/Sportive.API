using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _db;

    public DashboardService(AppDbContext db) => _db = db;

    public async Task<DashboardStatsDto> GetStatsAsync()
    {
        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var todayOrders = await _db.Orders
            .Where(o => o.CreatedAt.Date == today && o.Status != OrderStatus.Cancelled)
            .ToListAsync();

        var monthOrders = await _db.Orders
            .Where(o => o.CreatedAt >= monthStart && o.Status != OrderStatus.Cancelled)
            .ToListAsync();

        var allOrders = await _db.Orders
            .Where(o => o.Status != OrderStatus.Cancelled)
            .ToListAsync();

        var pendingCount = await _db.Orders
            .CountAsync(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Confirmed);

        var totalCustomers = await _db.Customers.CountAsync();
        var totalProducts = await _db.Products.CountAsync();
        var lowStock = await _db.ProductVariants.CountAsync(v => v.StockQuantity <= 5);

        return new DashboardStatsDto(
            TodaySales: todayOrders.Sum(o => o.TotalAmount),
            ThisMonthSales: monthOrders.Sum(o => o.TotalAmount),
            TotalRevenue: allOrders.Sum(o => o.TotalAmount),
            TotalOrders: allOrders.Count,
            PendingOrders: pendingCount,
            TodayOrders: todayOrders.Count,
            TotalCustomers: totalCustomers,
            TotalProducts: totalProducts,
            LowStockProducts: lowStock
        );
    }

    public async Task<List<SalesChartDto>> GetSalesChartAsync(string period)
    {
        var now = DateTime.UtcNow;

        if (period == "daily")
        {
            var from = now.AddDays(-29).Date;
            var orders = await _db.Orders
                .Where(o => o.CreatedAt.Date >= from && o.Status != OrderStatus.Cancelled)
                .GroupBy(o => o.CreatedAt.Date)
                .Select(g => new SalesChartDto(
                    g.Key.ToString("MM/dd"),
                    g.Sum(o => o.TotalAmount),
                    g.Count()
                ))
                .ToListAsync();
            return orders;
        }
        else if (period == "monthly")
        {
            var from = new DateTime(now.Year - 1, now.Month, 1);
            var orders = await _db.Orders
                .Where(o => o.CreatedAt >= from && o.Status != OrderStatus.Cancelled)
                .GroupBy(o => new { o.CreatedAt.Year, o.CreatedAt.Month })
                .Select(g => new SalesChartDto(
                    g.Key.Month + "/" + g.Key.Year,
                    g.Sum(o => o.TotalAmount),
                    g.Count()
                ))
                .ToListAsync();
            return orders;
        }
        else // yearly
        {
            var orders = await _db.Orders
                .Where(o => o.Status != OrderStatus.Cancelled)
                .GroupBy(o => o.CreatedAt.Year)
                .Select(g => new SalesChartDto(
                    g.Key.ToString(),
                    g.Sum(o => o.TotalAmount),
                    g.Count()
                ))
                .ToListAsync();
            return orders;
        }
    }

    public async Task<List<TopProductDto>> GetTopProductsAsync(int count = 10)
    {
        return await _db.Products
            .Include(p => p.Images)
            .Include(p => p.OrderItems)
            .Where(p => p.OrderItems.Any())
            .Select(p => new TopProductDto(
                p.Id,
                p.NameAr,
                p.NameEn,
                p.Images.Where(i => i.IsMain).Select(i => i.ImageUrl).FirstOrDefault(),
                p.OrderItems.Sum(i => i.Quantity),
                p.OrderItems.Sum(i => i.TotalPrice)
            ))
            .OrderByDescending(x => x.TotalSold)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<OrderStatusStatsDto>> GetOrderStatusStatsAsync()
    {
        var total = await _db.Orders.CountAsync();
        if (total == 0) return new List<OrderStatusStatsDto>();

        return await _db.Orders
            .GroupBy(o => o.Status)
            .Select(g => new OrderStatusStatsDto(
                g.Key.ToString(),
                g.Count(),
                (decimal)g.Count() / total * 100
            ))
            .ToListAsync();
    }

    public async Task<List<OrderSummaryDto>> GetRecentOrdersAsync(int count = 10)
    {
        return await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .Take(count)
            .Select(o => new OrderSummaryDto(
                o.Id, o.OrderNumber,
                o.Customer.FirstName + " " + o.Customer.LastName,
                o.Customer.Phone ?? "",
                o.Status.ToString(),
                o.FulfillmentType.ToString(),
                o.TotalAmount,
                o.CreatedAt,
                o.Items.Sum(i => i.Quantity)
            ))
            .ToListAsync();
    }
}
