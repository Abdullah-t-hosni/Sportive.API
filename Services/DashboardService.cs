using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Sportive.API.Utils;
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
    private readonly UserManager<AppUser> _userManager;

    public DashboardService(AppDbContext db, IHubContext<NotificationHub> hub, UserManager<AppUser> userManager)
    {
        _db = db;
        _hub = hub;
        _userManager = userManager;
    }

    public async Task<DashboardStatsDto> GetStatsAsync()
    {
        var now        = TimeHelper.GetEgyptTime();
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
        var totalCustomers = await _db.Customers.CountAsync();

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
            .CountAsync(c => c.CreatedAt < monthStart);

        // Growth Rates
        decimal CalculateGrowth(decimal current, decimal previous) => 
            previous == 0 ? (current > 0 ? 100 : 0) : Math.Round(((current - previous) / previous) * 100, 1);

        var todayGrowth = CalculateGrowth(todaySales, yesterdaySales);
        var monthSalesGrowth = CalculateGrowth(monthSales, lastMonthSales);
        var orderGrowth = CalculateGrowth(totalOrders, prevMonthOrders); // Comparing current total to last month's start total (Simplified but better than 0)
        var customerGrowth = CalculateGrowth(totalCustomers, prevCustomersCount);

        var uncollectedAmount = await _db.Orders
            .Where(o => o.PaymentStatus != PaymentStatus.Paid && o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        // الديون: هي الطلبات التي لم تُدفع بعد وليست ملغاة أو مرتجعة بالكامل (تشمل الآجل وأي طلب معلق الدفع)
        var debtAmount = await _db.Orders
            .Where(o => o.PaymentStatus != PaymentStatus.Paid && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        // المرتجعات: قيمة كل السلع التي تم إرجاعها (سواء مرتجع كامل أو جزئي)
        var returnAmount = await _db.OrderItems
            .Where(i => i.ReturnedQuantity > 0)
            .SumAsync(i => (decimal?)i.ReturnedQuantity * i.UnitPrice) ?? 0;

        var store = await _db.StoreInfo.AsNoTracking().FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        int lowStockThreshold = store?.LowStockThreshold ?? 5;

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
            TotalProducts: await _db.Products.CountAsync(),
            LowStockProducts: await _db.ProductVariants.CountAsync(v => v.StockQuantity <= lowStockThreshold && v.StockQuantity > 0),
            OutOfStockProducts: await _db.ProductVariants.CountAsync(v => v.StockQuantity == 0),
            UncollectedAmount: uncollectedAmount,
            DebtAmount: debtAmount,
            ReturnAmount: returnAmount
        );
    }

    public async Task<AnalyticsSummaryDto> GetAnalyticsSummaryAsync()
    {
        var now = TimeHelper.GetEgyptTime();
        var monthStart = new DateTime(now.Year, now.Month, 1);

        // Category Sales
        var catSales = await _db.OrderItems
            .Include(i => i.Product).ThenInclude(p => p!.Category)
            .GroupBy(i => new { 
                CategoryId = (i.Product != null && i.Product.Category != null) ? (int?)i.Product.Category.Id : null, 
                CategoryNameAr = (i.Product != null && i.Product.Category != null) ? i.Product.Category.NameAr : "Category Missing", 
                CategoryNameEn = (i.Product != null && i.Product.Category != null) ? i.Product.Category.NameEn : "Category Missing" 
            })
            .Select(g => new CategorySalesDto(
                g.Key.CategoryId ?? 0, g.Key.CategoryNameAr, g.Key.CategoryNameEn,
                g.Sum(i => i.Quantity),
                g.Sum(i => i.TotalPrice)
            ))
            .ToListAsync();

        // Daily Sales & New Customers for last 30 days
        var startDate = now.AddDays(-30);
        var orders = await _db.Orders
            .Where(o => o.CreatedAt >= startDate && o.Status != OrderStatus.Cancelled)
            .Select(o => new { o.CreatedAt, o.TotalAmount, o.Source }) // ✅ Added Source
            .ToListAsync();

        var newCustomers = await _db.Customers
            .Where(c => c.CreatedAt >= startDate)
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
        var query = _db.Orders.Include(o => o.Customer).AsQueryable();
        if (from.HasValue) query = query.Where(o => o.CreatedAt >= from.Value);
        if (to.HasValue)   query = query.Where(o => o.CreatedAt <= to.Value);

        var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
        
        var csv = new StringBuilder();
        csv.AppendLine("OrderNumber,Date,Customer,Email,Status,TotalAmount");
        foreach(var o in orders)
        {
            csv.AppendLine($"{o.OrderNumber},{o.CreatedAt:yyyy-MM-dd HH:mm},{o.Customer?.FullName},{o.Customer?.Email},{o.Status},{o.TotalAmount}");
        }
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    public async Task<AdvancedDashboardStatsDto> GetAdvancedStatsAsync()
    {
        var now = TimeHelper.GetEgyptTime();
        var thirtyDaysAgo = now.AddDays(-30);

        // 1. Sales by City (Heatmap)
        var salesByCityRaw = await _db.Orders
            .Where(o => o.Status != OrderStatus.Cancelled && o.DeliveryAddressId != null)
            .Select(o => new { City = o.DeliveryAddress!.City, o.TotalAmount }) 
            .GroupBy(x => x.City)
            .Select(g => new { 
                City = g.Key, 
                Count = g.Count(), 
                Total = g.Sum(x => (decimal?)x.TotalAmount) ?? 0 
            })
            .OrderByDescending(x => x.Total)
            .Take(10)
            .ToListAsync();

        var salesByCity = salesByCityRaw
            .Select(s => new LocationStatDto(s.City ?? "Unknown", s.Count, s.Total))
            .ToList();

        // 2. VIP Customers
        var topCustomersRaw = await _db.Customers
            .Include(c => c.Orders)
            .Select(c => new {
                c.Id,
                c.FullName,
                c.Email,
                TotalSpent = c.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => (decimal?)o.TotalAmount) ?? 0,
                OrderCount = c.Orders.Count(o => o.Status != OrderStatus.Cancelled)
            })
            .OrderByDescending(x => x.TotalSpent)
            .Take(10)
            .ToListAsync();

        var topCustomers = topCustomersRaw
            .Select(c => new VipCustomerDto(c.Id, c.FullName, c.Email, c.TotalSpent, c.OrderCount))
            .ToList();

        // 3. Inventory Insights (Run-out predictor & Stock health)
        // Calculating AvgDailySales based on last 30 days
        var recentSales = await _db.OrderItems
            .Where(i => i.CreatedAt >= thirtyDaysAgo)
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, TotalSold = g.Sum(i => i.Quantity) })
            .ToListAsync();

        var products = await _db.Products
            .Include(p => p.Variants)
            .Select(p => new { 
                p.Id, 
                p.NameEn, 
                Stock = p.Variants.Sum(v => v.StockQuantity) 
            })
            .ToListAsync();

        var inventoryInsights = products.Select(p => {
            var sold = recentSales.FirstOrDefault(s => s.ProductId == p.Id)?.TotalSold ?? 0;
            double avgDaily = Math.Round((double)sold / 30, 2);
            int? daysRemaining = avgDaily > 0 ? (int)Math.Floor(p.Stock / avgDaily) : null;
            return new InventoryIntelligenceDto(p.Id, p.NameEn, p.Stock, avgDaily, daysRemaining);
        })
        .OrderBy(x => x.DaysRemaining ?? 999)
        .Take(10)
        .ToList();

        // 4. Abandoned Carts
        var abandonedCartsRaw = await _db.CartItems
            .Include(c => c.Product)
            .Select(c => new { c.CustomerId, c.Quantity, Price = c.Product != null ? c.Product.Price : 0 })
            .GroupBy(c => c.CustomerId)
            .Select(g => new { 
                ItemCount = g.Sum(c => c.Quantity),
                PotentialRevenue = g.Sum(c => (decimal)c.Quantity * c.Price)
            })
            .ToListAsync();

        var abandonedCartStats = new AbandonedCartDto(
            abandonedCartsRaw.Count,
            abandonedCartsRaw.Sum(x => x.ItemCount),
            abandonedCartsRaw.Sum(x => x.PotentialRevenue)
        );

        // 5. Payment Methods
        var paymentMethodsRaw = await _db.Orders
            .Select(o => new { o.PaymentMethod, o.TotalAmount })
            .GroupBy(o => o.PaymentMethod)
            .Select(g => new {
                Method = g.Key,
                Count = g.Count(),
                Revenue = g.Sum(o => (decimal?)o.TotalAmount) ?? 0
            })
            .OrderByDescending(x => x.Revenue)
            .ToListAsync();

        var paymentMethods = paymentMethodsRaw
            .Select(p => new PaymentMethodStatDto(p.Method.ToString(), p.Count, p.Revenue))
            .ToList();

        // 6. Recent Admin Activity
        var recentActivityRaw = await _db.OrderStatusHistories
            .OrderByDescending(h => h.CreatedAt)
            .Take(15)
            .Select(h => new {
                h.ChangedByUserId,
                h.Status,
                h.OrderId,
                h.CreatedAt
            })
            .ToListAsync();

        var recentActivity = recentActivityRaw
            .Select(h => new AdminActivityDto(
                h.ChangedByUserId ?? "System",
                $"Changed Order status to {h.Status}",
                $"Order #{h.OrderId}",
                h.CreatedAt
            ))
            .ToList();

        // 7. Staff Performance (CASHIER/SALES TRACKING)
        var staffOrders = await _db.Orders
            .Where(o => !string.IsNullOrEmpty(o.SalesPersonId) && o.Status != OrderStatus.Cancelled)
            .GroupBy(o => o.SalesPersonId)
            .Select(g => new { StaffId = g.Key, Count = g.Count(), Total = g.Sum(o => (decimal?)o.TotalAmount) ?? 0 })
            .ToListAsync();

        var staffPerformanceItems = new List<StaffPerformanceDto>();
        foreach (var s in staffOrders)
        {
            var user = await _userManager.FindByIdAsync(s.StaffId!);
            var name = user?.FullName ?? "Unknown Staff";
            staffPerformanceItems.Add(new StaffPerformanceDto(s.StaffId!, name, s.Count, s.Total));
        }

        return new AdvancedDashboardStatsDto(
            SalesByCity: salesByCity,
            TopCustomers: topCustomers,
            InventoryInsights: inventoryInsights,
            AbandonedCarts: abandonedCartStats,
            PaymentMethods: paymentMethods,
            RecentActivity: recentActivity,
            StaffPerformance: staffPerformanceItems
        );
    }

    public async Task<StaffPerformanceDto> GetStaffStatsAsync(string staffId)
    {
        var orders = await _db.Orders
            .Where(o => o.SalesPersonId == staffId && o.Status != OrderStatus.Cancelled)
            .ToListAsync();

        var user = await _userManager.FindByIdAsync(staffId);
        var name = user?.FullName ?? "Unknown Staff";

        return new StaffPerformanceDto(
            staffId,
            name,
            orders.Count,
            orders.Sum(o => o.TotalAmount)
        );
    }

    public async Task TriggerLiveUpdateAsync()
    {
        // Pushes a refresh event to all clients in the "Admin" group
        await _hub.Clients.Group("Admin").SendAsync("RefreshDashboard");
    }

    public async Task<object> GetKpiAsync()
    {
        var now        = TimeHelper.GetEgyptTime();
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
            .Where(o => o.Status != OrderStatus.Cancelled)
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
            .Where(o => o.Status == OrderStatus.Returned && o.CreatedAt >= monthStart)
            .CountAsync();
        var returnRate = thisMonthOrders.Count > 0
            ? Math.Round((decimal)returnedThisMonth / (thisMonthOrders.Count + returnedThisMonth) * 100, 1) : 0;

        // ── TOP PRODUCTS (أفضل 10 منتجات) ───────────
        var topProducts = await _db.OrderItems
            .Include(i => i.Order)
            .Include(i => i.Product).ThenInclude(p => p!.Images)
            .Where(i => i.Order.Status != OrderStatus.Cancelled
                     && i.Order.CreatedAt >= monthStart
                     && i.ProductId.HasValue) // ✅ Added
            .GroupBy(i => new { ProductId = i.ProductId!.Value, i.ProductNameAr, i.ProductNameEn })
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
            .Where(img => productIds.Contains(img.ProductId) && img.IsMain)
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
            .CountAsync(c => c.CreatedAt >= monthStart);
        var returningCustomers = thisMonthCustomers - newCustomers < 0 ? 0 : thisMonthCustomers - newCustomers;

        // ── HOURLY PEAK (أوقات الذروة) ────────────────
        var peakHour = salesByHour.OrderByDescending(h => h.revenue).First();

        // ── ASSEMBLE RESPONSE ─────────────────────────
        return new {
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
                image = images.GetValueOrDefault(p.ProductId), // Works now if ProductId is forced to int above
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
        };
    }

    private static decimal GrowthPct(decimal current, decimal previous) =>
        previous == 0 ? (current > 0 ? 100 : 0) :
        Math.Round((current - previous) / previous * 100, 1);

    // --- Original Methods (Modified slightly for consistency) ---

    public async Task<List<SalesChartDto>> GetSalesChartAsync(string period)
    {
        var now = TimeHelper.GetEgyptTime();
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
        var items = await _db.OrderItems.Include(i => i.Product!).ThenInclude(p => p.Images)
            .Select(i => new { 
                i.ProductId, i.ProductNameAr, i.ProductNameEn, i.Quantity, i.TotalPrice, 
                MainImage = i.Product != null ? i.Product.Images.Where(img => img.IsMain).Select(img => img.ImageUrl).FirstOrDefault() : null 
            })
            .ToListAsync();

        return items.GroupBy(i => i.ProductId).Select(g => new TopProductDto(g.Key, g.First().ProductNameAr, g.First().ProductNameEn, g.First().MainImage, g.Sum(i => i.Quantity), g.Sum(i => i.TotalPrice))).OrderByDescending(x => x.TotalSold).Take(count).ToList();
    }

    public async Task<List<OrderStatusStatsDto>> GetOrderStatusStatsAsync()
    {
        var total = await _db.Orders.CountAsync();
        if (total == 0) return new List<OrderStatusStatsDto>();
        var groups = await _db.Orders.GroupBy(o => o.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        return groups.Select(g => new OrderStatusStatsDto(g.Status.ToString(), g.Count, Math.Round((decimal)g.Count / total * 100, 1))).ToList();
    }

    public async Task<List<OrderSummaryDto>> GetRecentOrdersAsync(int count = 10)
    {
        return await _db.Orders.Include(o => o.Customer).Include(o => o.Items).OrderByDescending(o => o.CreatedAt).Take(count)
            .Select(o => new OrderSummaryDto(
                o.Id, o.OrderNumber, o.Customer.FullName, 
                o.Customer.Phone ?? "", o.Status.ToString(), o.FulfillmentType.ToString(), 
                o.TotalAmount, o.CreatedAt, o.Items.Sum(i => i.Quantity), 
                o.Source.ToString(), o.PaymentMethod.ToString(), o.PaymentStatus.ToString(), 
                o.AdminNotes, o.CouponCode))
            .ToListAsync();
    }
}
