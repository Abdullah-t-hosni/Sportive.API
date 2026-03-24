using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AnalyticsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AnalyticsController(AppDbContext db) => _db = db;

    /// <summary>GET /api/analytics/admin-stats — إحصائيات شاملة</summary>
    [HttpGet("admin-stats")]
    public async Task<IActionResult> GetAdminStats()
    {
        var now        = DateTime.UtcNow;
        var todayStart = now.Date;
        var todayEnd   = todayStart.AddDays(1);
        var weekStart  = todayStart.AddDays(-7);
        var monthStart = new DateTime(now.Year, now.Month, 1);

        // مبيعات
        var todayRevenue = await _db.Orders
            .Where(o => o.CreatedAt >= todayStart && o.CreatedAt < todayEnd
                     && o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var weekRevenue = await _db.Orders
            .Where(o => o.CreatedAt >= weekStart && o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var monthRevenue = await _db.Orders
            .Where(o => o.CreatedAt >= monthStart && o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        // طلبات
        var todayOrders  = await _db.Orders.CountAsync(o => o.CreatedAt >= todayStart && o.CreatedAt < todayEnd);
        var weekOrders   = await _db.Orders.CountAsync(o => o.CreatedAt >= weekStart);
        var monthOrders  = await _db.Orders.CountAsync(o => o.CreatedAt >= monthStart);

        // عملاء جدد
        var newCustomersToday = await _db.Customers.CountAsync(c => c.CreatedAt >= todayStart && c.CreatedAt < todayEnd && !c.IsDeleted);
        var newCustomersWeek  = await _db.Customers.CountAsync(c => c.CreatedAt >= weekStart && !c.IsDeleted);

        // منتجات
        var totalProducts  = await _db.Products.CountAsync(p => !p.IsDeleted);
        var lowStockCount  = await _db.ProductVariants.CountAsync(v => !v.IsDeleted && v.StockQuantity <= 5 && v.StockQuantity > 0);
        var outOfStockCount = await _db.ProductVariants.CountAsync(v => !v.IsDeleted && v.StockQuantity == 0);

        // منتجات ذات مخزون منخفض (التفاصيل للعرض)
        var lowStockProducts = await _db.ProductVariants
            .Include(v => v.Product).ThenInclude(p => p.Images)
            .Where(v => !v.IsDeleted && v.StockQuantity <= 5 && v.StockQuantity > 0)
            .Select(v => new {
                v.Id,
                ProductId = v.Product.Id,
                v.Product.NameAr,
                v.Product.NameEn,
                v.StockQuantity,
                ImageUrl = v.Product.Images
                    .Where(img => img.IsMain && !img.IsDeleted)
                    .Select(img => img.ImageUrl)
                    .FirstOrDefault()
            })
            .Take(10)
            .ToListAsync();

        // أكثر منتجات مبيعاً هذا الشهر
        var topProductsRaw = await _db.OrderItems
            .Include(i => i.Product)
            .Where(i => !i.IsDeleted && i.Order.CreatedAt >= monthStart)
            .GroupBy(i => new { i.ProductId, i.ProductNameAr, i.ProductNameEn })
            .Select(g => new {
                ProductId = g.Key.ProductId,
                NameAr    = g.Key.ProductNameAr,
                NameEn    = g.Key.ProductNameEn,
                TotalSold    = g.Sum(i => i.Quantity),
                TotalRevenue = g.Sum(i => i.TotalPrice)
            })
            .OrderByDescending(x => x.TotalSold)
            .Take(5)
            .ToListAsync();

        // توزيع الطلبات حسب الحالة
        var ordersByStatus = await _db.Orders
            .Where(o => !o.IsDeleted)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        return Ok(new {
            revenue = new {
                today  = todayRevenue,
                week   = weekRevenue,
                month  = monthRevenue
            },
            orders = new {
                today  = todayOrders,
                week   = weekOrders,
                month  = monthOrders
            },
            customers = new {
                newToday = newCustomersToday,
                newWeek  = newCustomersWeek,
                total    = await _db.Customers.CountAsync(c => !c.IsDeleted)
            },
            products = new {
                total      = totalProducts,
                lowStock   = lowStockCount,
                outOfStock = outOfStockCount
            },
            lowStockProducts = lowStockProducts,
            topProducts    = topProductsRaw,
            ordersByStatus = ordersByStatus
        });
    }
}
