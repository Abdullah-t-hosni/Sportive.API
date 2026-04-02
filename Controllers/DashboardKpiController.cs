using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

/// <summary>
/// GET /api/dashboard/kpi
/// مؤشرات KPI متقدمة — داشبورد شامل
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class DashboardKpiController : ControllerBase
{
    private readonly IDashboardService _dashboard;
    private readonly AppDbContext _db;

    public DashboardKpiController(IDashboardService dashboard, AppDbContext db)
    {
        _dashboard = dashboard;
        _db = db;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats() =>
        Ok(await _dashboard.GetStatsAsync());

    [HttpGet("sales-chart")]
    public async Task<IActionResult> GetSalesChart([FromQuery] string period = "monthly") =>
        Ok(await _dashboard.GetSalesChartAsync(period));

    [HttpGet("top-products")]
    public async Task<IActionResult> GetTopProductsList([FromQuery] int count = 10) =>
        Ok(await _dashboard.GetTopProductsAsync(count));

    [HttpGet("order-status-stats")]
    public async Task<IActionResult> GetOrderStatusStats() =>
        Ok(await _dashboard.GetOrderStatusStatsAsync());

    [HttpGet("recent-orders")]
    public async Task<IActionResult> GetRecentOrders([FromQuery] int count = 10) =>
        Ok(await _dashboard.GetRecentOrdersAsync(count));

    [HttpGet("analytics-summary")]
    public async Task<IActionResult> GetAnalyticsSummary() =>
        Ok(await _dashboard.GetAnalyticsSummaryAsync());

    [HttpGet("export-sales")]
    public async Task<IActionResult> ExportSales([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var csvBytes = await _dashboard.ExportSalesToCsvAsync(from, to);
        return File(csvBytes, "text/csv", $"sales-report-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet("advanced-stats")]
    public async Task<IActionResult> GetAdvancedStats() =>
        Ok(await _dashboard.GetAdvancedStatsAsync());

    [HttpGet("staff-stats")]
    public async Task<IActionResult> GetStaffStats([FromQuery] string staffId) =>
        Ok(await _dashboard.GetStaffStatsAsync(staffId));

    // ✅ Compatibility Aliases
    [HttpGet("/api/analytics/admin-stats")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> GetStatsAlias() =>
        Ok(await _dashboard.GetStatsAsync());

    [HttpGet("/api/analytics/summary")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> GetAnalyticsSummaryAlias() =>
        Ok(await _dashboard.GetAnalyticsSummaryAsync());

    [HttpPost("trigger-update")]
    public async Task<IActionResult> TriggerUpdate()
    {
        await _dashboard.TriggerLiveUpdateAsync();
        return Ok();
    }

    [HttpGet("kpi")]
    public async Task<IActionResult> GetKpi()
    {
        var now       = DateTime.UtcNow;
        var todayStart     = now.Date;
        var todayEnd       = todayStart.AddDays(1);
        var yesterdayStart = todayStart.AddDays(-1);
        var weekStart      = todayStart.AddDays(-7);
        var lastWeekStart  = todayStart.AddDays(-14);
        var lastWeekEnd    = todayStart.AddDays(-7);
        var monthStart     = new DateTime(now.Year, now.Month, 1);
        var lastMonthStart = monthStart.AddMonths(-1);
        var lastMonthEnd   = monthStart;
        var yearStart      = new DateTime(now.Year, 1, 1);

        // ── جلب كل الطلبات المهمة مرة واحدة ──────────
        var allOrders = await _db.Orders
            .Where(o => !o.IsDeleted && o.Status != OrderStatus.Cancelled)
            .Select(o => new {
                o.Id, o.CreatedAt, o.TotalAmount, o.SubTotal,
                o.DiscountAmount, o.Status, o.Source,
                o.PaymentMethod, o.CustomerId,
                ItemCount = o.Items.Sum(i => i.Quantity)
            })
            .ToListAsync();

        // ── KPI 1: إيرادات اليوم ──────────────────────
        var todayOrders     = allOrders.Where(o => o.CreatedAt >= todayStart && o.CreatedAt < todayEnd).ToList();
        var yesterdayOrders = allOrders.Where(o => o.CreatedAt >= yesterdayStart && o.CreatedAt < todayStart).ToList();
        var todayRevenue    = todayOrders.Sum(o => o.TotalAmount);
        var yesterdayRevenue= yesterdayOrders.Sum(o => o.TotalAmount);

        // ── KPI 2: هذا الأسبوع vs الأسبوع الماضي ─────
        var thisWeekOrders  = allOrders.Where(o => o.CreatedAt >= weekStart).ToList();
        var lastWeekOrders  = allOrders.Where(o => o.CreatedAt >= lastWeekStart && o.CreatedAt < lastWeekEnd).ToList();
        var thisWeekRevenue = thisWeekOrders.Sum(o => o.TotalAmount);
        var lastWeekRevenue = lastWeekOrders.Sum(o => o.TotalAmount);

        // ── KPI 3: هذا الشهر ─────────────────────────
        var thisMonthOrders = allOrders.Where(o => o.CreatedAt >= monthStart).ToList();
        var lastMonthOrders = allOrders.Where(o => o.CreatedAt >= lastMonthStart && o.CreatedAt < lastMonthEnd).ToList();
        var thisMonthRevenue= thisMonthOrders.Sum(o => o.TotalAmount);
        var lastMonthRevenue= lastMonthOrders.Sum(o => o.TotalAmount);

        // ── KPI 4: متوسط قيمة الطلب ──────────────────
        var avgOrderThisWeek = thisWeekOrders.Count > 0 ? thisWeekOrders.Average(o => o.TotalAmount) : 0;
        var avgOrderLastWeek = lastWeekOrders.Count > 0 ? lastWeekOrders.Average(o => o.TotalAmount) : 0;

        // ── KPI 5: معدل التحويل كاشير vs موقع ─────────
        var posCount     = thisWeekOrders.Count(o => o.Source == OrderSource.POS);
        var webCount     = thisWeekOrders.Count(o => o.Source == OrderSource.Website);
        var totalCount   = thisWeekOrders.Count;

        // ── KPI 6: المرتجعات ──────────────────────────
        var returnedThisMonth = await _db.Orders
            .Where(o => !o.IsDeleted && o.Status == OrderStatus.Returned && o.CreatedAt >= monthStart)
            .CountAsync();
        var returnRate = thisMonthOrders.Count > 0
            ? Math.Round((decimal)returnedThisMonth / (thisMonthOrders.Count + returnedThisMonth) * 100, 1) : 0;

        // ── TOP PRODUCTS (أفضل 10 منتجات) ───────────
        var topProducts = await _db.OrderItems
            .Include(i => i.Order)
            .Include(i => i.Product).ThenInclude(p => p.Images)
            .Where(i => !i.IsDeleted && !i.Order.IsDeleted
                     && i.Order.Status != OrderStatus.Cancelled
                     && i.Order.CreatedAt >= monthStart)
            .GroupBy(i => new { i.ProductId, i.ProductNameAr, i.ProductNameEn })
            .Select(g => new {
                g.Key.ProductId,
                g.Key.ProductNameAr,
                g.Key.ProductNameEn,
                TotalSold    = g.Sum(i => i.Quantity),
                TotalRevenue = g.Sum(i => i.TotalPrice),
                OrderCount   = g.Select(i => i.OrderId).Distinct().Count(),
            })
            .OrderByDescending(x => x.TotalRevenue)
            .Take(10)
            .ToListAsync();

        // Add images
        var productIds = topProducts.Select(p => p.ProductId).ToList();
        var images     = await _db.ProductImages
            .Where(img => productIds.Contains(img.ProductId) && img.IsMain && !img.IsDeleted)
            .ToDictionaryAsync(img => img.ProductId, img => img.ImageUrl);

        // ── SALES BY HOUR (آخر 24 ساعة) ──────────────
        var last24hOrders = allOrders.Where(o => o.CreatedAt >= now.AddHours(-24)).ToList();
        var salesByHour   = Enumerable.Range(0, 24).Select(h => {
            var hourStart = now.AddHours(-24 + h);
            var hourEnd   = hourStart.AddHours(1);
            var hrs       = last24hOrders.Where(o => o.CreatedAt >= hourStart && o.CreatedAt < hourEnd);
            return new { hour = hourStart.Hour, revenue = hrs.Sum(o => o.TotalAmount), orders = hrs.Count() };
        }).ToList();

        // ── SALES BY DAY (آخر 30 يوم) ────────────────
        var salesByDay = Enumerable.Range(0, 30).Select(d => {
            var dayStart = todayStart.AddDays(-29 + d);
            var dayEnd   = dayStart.AddDays(1);
            var day      = allOrders.Where(o => o.CreatedAt >= dayStart && o.CreatedAt < dayEnd);
            return new {
                date    = dayStart.ToString("MM/dd"),
                dayName = dayStart.DayOfWeek.ToString()[..3],
                revenue = day.Sum(o => o.TotalAmount),
                orders  = day.Count()
            };
        }).ToList();

        // ── PAYMENT METHOD BREAKDOWN ──────────────────
        var paymentBreakdown = thisMonthOrders
            .GroupBy(o => o.PaymentMethod.ToString())
            .Select(g => new {
                method  = g.Key,
                count   = g.Count(),
                revenue = g.Sum(o => o.TotalAmount),
                pct     = totalCount > 0 ? Math.Round((decimal)g.Count() / thisMonthOrders.Count * 100, 1) : 0
            })
            .OrderByDescending(x => x.revenue)
            .ToList();

        // ── NEW vs RETURNING CUSTOMERS ────────────────
        var thisMonthCustomers = thisMonthOrders.Select(o => o.CustomerId).Distinct().Count();
        var newCustomers = await _db.Customers
            .CountAsync(c => !c.IsDeleted && c.CreatedAt >= monthStart);
        var returningCustomers = thisMonthCustomers - newCustomers < 0 ? 0 : thisMonthCustomers - newCustomers;

        // ── HOURLY PEAK (أوقات الذروة) ────────────────
        var peakHour = salesByHour.OrderByDescending(h => h.revenue).First();

        // ── ASSEMBLE RESPONSE ─────────────────────────
        return Ok(new {
            generatedAt = now,

            // اليوم مقارنة بالأمس
            today = new {
                revenue     = todayRevenue,
                orders      = todayOrders.Count,
                avgOrder    = todayOrders.Count > 0 ? Math.Round(todayRevenue / todayOrders.Count, 2) : 0,
                vsYesterday = new {
                    revenue  = yesterdayRevenue,
                    growth   = GrowthPct(todayRevenue, yesterdayRevenue),
                    orders   = yesterdayOrders.Count,
                    isUp     = todayRevenue >= yesterdayRevenue
                }
            },

            // هذا الأسبوع مقارنة بالأسبوع الماضي
            thisWeek = new {
                revenue     = thisWeekRevenue,
                orders      = thisWeekOrders.Count,
                avgOrder    = Math.Round(avgOrderThisWeek, 2),
                posOrders   = posCount,
                webOrders   = webCount,
                posRevenue  = thisWeekOrders.Where(o => o.Source == OrderSource.POS).Sum(o => o.TotalAmount),
                webRevenue  = thisWeekOrders.Where(o => o.Source == OrderSource.Website).Sum(o => o.TotalAmount),
                vsLastWeek  = new {
                    revenue  = lastWeekRevenue,
                    growth   = GrowthPct(thisWeekRevenue, lastWeekRevenue),
                    orders   = lastWeekOrders.Count,
                    avgOrder = Math.Round(avgOrderLastWeek, 2),
                    isUp     = thisWeekRevenue >= lastWeekRevenue
                }
            },

            // هذا الشهر
            thisMonth = new {
                revenue        = thisMonthRevenue,
                orders         = thisMonthOrders.Count,
                customers      = thisMonthCustomers,
                newCustomers,
                returningCustomers,
                returnedOrders = returnedThisMonth,
                returnRate,
                totalDiscount  = thisMonthOrders.Sum(o => o.DiscountAmount),
                vsLastMonth    = new {
                    revenue = lastMonthRevenue,
                    growth  = GrowthPct(thisMonthRevenue, lastMonthRevenue),
                    orders  = lastMonthOrders.Count,
                    isUp    = thisMonthRevenue >= lastMonthRevenue
                }
            },

            // أفضل المنتجات
            topProducts = topProducts.Select(p => new {
                p.ProductId, p.ProductNameAr, p.ProductNameEn,
                p.TotalSold, p.TotalRevenue, p.OrderCount,
                image = images.GetValueOrDefault(p.ProductId),
            }),

            // مخطط المبيعات
            charts = new {
                byHour = salesByHour,
                byDay  = salesByDay,
            },

            // توزيع طرق الدفع
            paymentBreakdown,

            // إحصائيات إضافية
            insights = new {
                peakHour       = peakHour.hour,
                peakHourRevenue= peakHour.revenue,
                bestDayThisWeek= salesByDay.TakeLast(7).OrderByDescending(d => d.revenue).First().date,
                totalYearRevenue = allOrders.Where(o => o.CreatedAt >= yearStart).Sum(o => o.TotalAmount),
            }
        });
    }

    private static decimal GrowthPct(decimal current, decimal previous) =>
        previous == 0 ? (current > 0 ? 100 : 0) :
        Math.Round((current - previous) / previous * 100, 1);
}
