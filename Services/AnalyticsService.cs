using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Services;

public record DashboardStats(
    decimal TotalRevenue, int OrderCount, int CustomerCount, 
    List<CategorySales> SalesByCategory, List<DailySales> Last30DaysSales,
    List<TopProduct> TopProducts, List<LowStockAlert> LowStockAlerts
);

public record CategorySales(string Name, decimal Total);
public record DailySales(DateTime Date, decimal Total);
public record TopProduct(int Id, string Name, int SoldCount, decimal Revenue);
public record LowStockAlert(int ProductId, string ProductName, string? Size, string? Color, int Remaining);

public interface IAnalyticsService
{
    Task<DashboardStats> GetAdminStatsAsync();
}

public class AnalyticsService : IAnalyticsService
{
    private readonly AppDbContext _db;
    public AnalyticsService(AppDbContext db) => _db = db;

    public async Task<DashboardStats> GetAdminStatsAsync()
    {
        var orders = await _db.Orders.Where(o => o.Status != OrderStatus.Cancelled).ToListAsync();
        
        var totalRev = orders.Sum(o => o.TotalAmount);
        var orderCount = orders.Count;
        var custCount = await _db.Customers.CountAsync();

        // Sales by Category
        var salesByCat = await _db.OrderItems
            .Include(i => i.Product).ThenInclude(p => p.Category)
            .Where(i => i.Order.Status != OrderStatus.Cancelled)
            .GroupBy(i => i.Product.Category.NameEn)
            .Select(g => new CategorySales(g.Key, g.Sum(x => x.TotalPrice)))
            .ToListAsync();

        // Daily Sales (Last 30 Days)
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        var dailySales = orders
            .Where(o => o.CreatedAt >= thirtyDaysAgo)
            .GroupBy(o => o.CreatedAt.Date)
            .Select(g => new DailySales(g.Key, g.Sum(x => x.TotalAmount)))
            .OrderBy(x => x.Date)
            .ToList();

        // Top Selling Products
        var topProducts = await _db.OrderItems
            .Include(i => i.Product)
            .Where(i => i.Order.Status != OrderStatus.Cancelled)
            .GroupBy(i => new { i.ProductId, i.Product.NameAr })
            .Select(g => new TopProduct(g.Key.ProductId, g.Key.NameAr, g.Sum(x => x.Quantity), g.Sum(x => x.TotalPrice)))
            .OrderByDescending(x => x.SoldCount)
            .Take(5)
            .ToListAsync();

        // Low Stock Alerts (threshold: < 5 items)
        var lowStock = await _db.ProductVariants
            .Include(v => v.Product)
            .Where(v => v.StockQuantity < 5)
            .Select(v => new LowStockAlert(v.ProductId, v.Product.NameAr, v.Size, v.Color, v.StockQuantity))
            .ToListAsync();

        return new DashboardStats(totalRev, orderCount, custCount, salesByCat, dailySales, topProducts, lowStock);
    }
}
