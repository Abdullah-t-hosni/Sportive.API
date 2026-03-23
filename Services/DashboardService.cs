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
        var now        = DateTime.UtcNow;
        var todayStart = now.Date;
        var todayEnd   = todayStart.AddDays(1);
        var monthStart = new DateTime(now.Year, now.Month, 1);

        // MySQL-safe: avoid .Date in LINQ — use range comparisons instead
        var todaySales = await _db.Orders
            .Where(o => o.CreatedAt >= todayStart && o.CreatedAt < todayEnd
                     && o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var todayOrderCount = await _db.Orders
            .CountAsync(o => o.CreatedAt >= todayStart && o.CreatedAt < todayEnd
                          && o.Status != OrderStatus.Cancelled);

        var monthSales = await _db.Orders
            .Where(o => o.CreatedAt >= monthStart && o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var totalRevenue = await _db.Orders
            .Where(o => o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var totalOrders = await _db.Orders
            .CountAsync(o => o.Status != OrderStatus.Cancelled);

        var pendingOrders = await _db.Orders
            .CountAsync(o => o.Status == OrderStatus.Pending
                          || o.Status == OrderStatus.Confirmed);

        var totalCustomers = await _db.Customers.CountAsync(c => !c.IsDeleted);
        var totalProducts  = await _db.Products.CountAsync(p => !p.IsDeleted);
        var lowStock       = await _db.ProductVariants
            .CountAsync(v => !v.IsDeleted && v.StockQuantity <= 5);

        return new DashboardStatsDto(
            TodaySales:       todaySales,
            ThisMonthSales:   monthSales,
            TotalRevenue:     totalRevenue,
            TotalOrders:      totalOrders,
            PendingOrders:    pendingOrders,
            TodayOrders:      todayOrderCount,
            TotalCustomers:   totalCustomers,
            TotalProducts:    totalProducts,
            LowStockProducts: lowStock
        );
    }

    public async Task<List<SalesChartDto>> GetSalesChartAsync(string period)
    {
        var now = DateTime.UtcNow;

        if (period == "daily")
        {
            var from = now.AddDays(-29).Date;
            // Pull data to memory then group (MySQL doesn't support .Date in GroupBy well)
            var orders = await _db.Orders
                .Where(o => o.CreatedAt >= from && o.Status != OrderStatus.Cancelled)
                .Select(o => new { o.CreatedAt, o.TotalAmount })
                .ToListAsync();

            return orders
                .GroupBy(o => o.CreatedAt.Date)
                .Select(g => new SalesChartDto(
                    g.Key.ToString("MM/dd"),
                    g.Sum(o => o.TotalAmount),
                    g.Count()
                ))
                .OrderBy(x => x.Label)
                .ToList();
        }
        else if (period == "monthly")
        {
            var from = new DateTime(now.Year - 1, now.Month, 1);
            var orders = await _db.Orders
                .Where(o => o.CreatedAt >= from && o.Status != OrderStatus.Cancelled)
                .Select(o => new { o.CreatedAt, o.TotalAmount })
                .ToListAsync();

            return orders
                .GroupBy(o => new { o.CreatedAt.Year, o.CreatedAt.Month })
                .Select(g => new SalesChartDto(
                    $"{g.Key.Month}/{g.Key.Year}",
                    g.Sum(o => o.TotalAmount),
                    g.Count()
                ))
                .OrderBy(x => x.Label)
                .ToList();
        }
        else // yearly
        {
            var orders = await _db.Orders
                .Where(o => o.Status != OrderStatus.Cancelled)
                .Select(o => new { o.CreatedAt, o.TotalAmount })
                .ToListAsync();

            return orders
                .GroupBy(o => o.CreatedAt.Year)
                .Select(g => new SalesChartDto(
                    g.Key.ToString(),
                    g.Sum(o => o.TotalAmount),
                    g.Count()
                ))
                .OrderBy(x => x.Label)
                .ToList();
        }
    }

    public async Task<List<TopProductDto>> GetTopProductsAsync(int count = 10)
    {
        // Pull to memory to avoid MySQL GroupBy issues
        var items = await _db.OrderItems
            .Include(i => i.Product).ThenInclude(p => p.Images)
            .Where(i => !i.IsDeleted)
            .Select(i => new
            {
                i.ProductId,
                i.ProductNameAr,
                i.ProductNameEn,
                i.Quantity,
                i.TotalPrice,
                MainImage = i.Product.Images
                    .Where(img => img.IsMain && !img.IsDeleted)
                    .Select(img => img.ImageUrl)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return items
            .GroupBy(i => i.ProductId)
            .Select(g => new TopProductDto(
                g.Key,
                g.First().ProductNameAr,
                g.First().ProductNameEn,
                g.First().MainImage,
                g.Sum(i => i.Quantity),
                g.Sum(i => i.TotalPrice)
            ))
            .OrderByDescending(x => x.TotalSold)
            .Take(count)
            .ToList();
    }

    public async Task<List<OrderStatusStatsDto>> GetOrderStatusStatsAsync()
    {
        var total = await _db.Orders.CountAsync(o => !o.IsDeleted);
        if (total == 0) return new List<OrderStatusStatsDto>();

        var groups = await _db.Orders
            .Where(o => !o.IsDeleted)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        return groups
            .Select(g => new OrderStatusStatsDto(
                g.Status.ToString(),
                g.Count,
                Math.Round((decimal)g.Count / total * 100, 1)
            ))
            .ToList();
    }

    public async Task<List<OrderSummaryDto>> GetRecentOrdersAsync(int count = 10)
    {
        return await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Where(o => !o.IsDeleted)
            .OrderByDescending(o => o.CreatedAt)
            .Take(count)
            .Select(o => new OrderSummaryDto(
                o.Id,
                o.OrderNumber,
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
