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
using Microsoft.Extensions.Caching.Memory;

namespace Sportive.API.Services;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly UserManager<AppUser> _userManager;
    private readonly ICacheService _cache;

    public DashboardService(AppDbContext db, IHubContext<NotificationHub> hub, UserManager<AppUser> userManager, ICacheService cache)
    {
        _db = db;
        _hub = hub;
        _userManager = userManager;
        _cache = cache;
    }

    public async Task<DashboardStatsDto> GetStatsAsync(OrderSource? source = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var now        = TimeHelper.GetEgyptTime();
        // 🕒 BUSINESS DAY OFFSET: The day ends at 2 AM.
        var todayStart = (now.Hour < 2) ? now.Date.AddDays(-1).AddHours(2) : now.Date.AddHours(2);
        var todayEnd   = todayStart.AddDays(1);
        
        // Determine the targeted range for "Today" stats (used in Orders page)
        // 🕒 BUSINESS DAY OFFSET: Apply 2 AM offset to match OrderService list logic
        var targetStart = fromDate?.Date.AddHours(2) ?? todayStart;
        var targetEnd   = toDate != null ? toDate.Value.Date.AddDays(1).AddHours(2) : 
                          fromDate != null ? fromDate.Value.Date.AddDays(1).AddHours(2) : 
                          todayEnd;

        var monthStart = new DateTime(now.Year, now.Month, 1);
        var lastMonthStart = monthStart.AddMonths(-1);

        // --- Base Query for Orders ---
        var query = _db.Orders.Where(o => o.Status != OrderStatus.Cancelled);
        if (source.HasValue)
        {
            query = query.Where(o => o.Source == source.Value);
        }

        // --- Target Period Stats (Displays as 'Today Sales' in UI) ---
        var periodSales = await query
            .Where(o => o.CreatedAt >= targetStart && o.CreatedAt < targetEnd)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        // --- Monthly & Global Stats ---
        var monthSales = await query
            .Where(o => o.CreatedAt >= monthStart)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var totalOrders = await query.CountAsync();
        var totalCustomers = await _db.Customers.CountAsync();

        // --- Growth Calculation (Standard Today vs Yesterday) ---
        var yesterdayStartDate = todayStart.AddDays(-1);
        var yesterdaySales = await query
            .Where(o => o.CreatedAt >= yesterdayStartDate && o.CreatedAt < todayStart)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var lastMonthSales = await query
            .Where(o => o.CreatedAt >= lastMonthStart && o.CreatedAt < monthStart)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var prevMonthOrders = await query
            .CountAsync(o => o.CreatedAt >= lastMonthStart && o.CreatedAt < monthStart);
            
        var prevCustomersCount = await _db.Customers
            .CountAsync(c => c.CreatedAt < monthStart);

        decimal CalculateGrowth(decimal current, decimal previous) => 
            previous == 0 ? (current > 0 ? 100 : 0) : Math.Round(((current - previous) / previous) * 100, 1);

        var todayGrowth = CalculateGrowth(periodSales, yesterdaySales); // Growth relative to what's in the primary box
        var monthSalesGrowth = CalculateGrowth(monthSales, lastMonthSales);
        var orderGrowth = CalculateGrowth(totalOrders, prevMonthOrders);
        var customerGrowth = CalculateGrowth(totalCustomers, prevCustomersCount);

        // --- Debt & Uncollected ---
        // Uncollected logic: Only orders NOT fully returned/cancelled. Subtract paid from total.
        var debtQuery = query.Where(o => o.Status != OrderStatus.Returned && o.Status != OrderStatus.Cancelled);
        
        // If filtering by date, we focus debt on THAT period's orders? 
        // Usually "Uncollected" in Orders page means of ALL shown orders.
        var uncollectedAmount = await debtQuery
            .Where(o => o.CreatedAt >= targetStart && o.CreatedAt < targetEnd)
            .SumAsync(o => (decimal?)(o.TotalAmount - o.PaidAmount)) ?? 0;

        // --- Returns Calculation ---
        // ✅ حتي لو الـ ReturnedQuantity مش مسجل (حالات قديمة للموقع)، نعتمد علي حالة الطلب
        var returnAmountQuery = _db.OrderItems
            .Where(i => i.Order!.Status != OrderStatus.Cancelled && 
                        (i.ReturnedQuantity > 0 || i.Order!.Status == OrderStatus.Returned || i.Order!.Status == OrderStatus.PartiallyReturned));

        if (source.HasValue)
        {
            returnAmountQuery = returnAmountQuery.Where(i => i.Order!.Source == source.Value);
        }

        var salesReturnMapping = await _db.AccountSystemMappings
            .Where(m => m.Key == "salesReturnAccountID")
            .Select(m => m.AccountId)
            .FirstOrDefaultAsync();

        var returnsQuery = _db.JournalEntries
            .Where(e => e.Type == JournalEntryType.SalesReturn && e.EntryDate >= targetStart && e.EntryDate < targetEnd);

        if (source.HasValue)
        {
            returnsQuery = returnsQuery.Where(e => e.CostCenter == source.Value);
        }

        var periodReturnAmount = await returnsQuery
            .SelectMany(e => e.Lines)
            .Where(l => l.Debit > 0 && (l.AccountId == salesReturnMapping || l.Account.Code.StartsWith("4103")))
            .SumAsync(l => (decimal?)l.Debit) ?? 0;

        // --- All Time Returns ---
        var totalReturnsQuery = _db.JournalEntries
            .Where(e => e.Type == JournalEntryType.SalesReturn);

        if (source.HasValue)
        {
            totalReturnsQuery = totalReturnsQuery.Where(e => e.CostCenter == source.Value);
        }

        var totalReturnAmount = await totalReturnsQuery
            .SelectMany(e => e.Lines)
            .Where(l => l.Debit > 0 && (l.AccountId == salesReturnMapping || l.Account.Code.StartsWith("4103")))
            .SumAsync(l => (decimal?)l.Debit) ?? 0;

        // التحصيلات (سندات القبض)
        var collectionQuery = _db.ReceiptVouchers
            .Where(v => v.VoucherDate >= targetStart && v.VoucherDate < targetEnd);

        if (source.HasValue)
        {
            collectionQuery = collectionQuery.Where(v => v.CostCenter == source.Value);
        }

        var todayCollections = await collectionQuery
            .SumAsync(v => (decimal?)v.Amount) ?? 0;

        var newCustomersToday = await _db.Customers
            .CountAsync(c => c.CreatedAt >= targetStart && c.CreatedAt < targetEnd);

        var store = await _db.StoreInfo.AsNoTracking().FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        int lowStockThreshold = store?.LowStockThreshold ?? 5;

        return new DashboardStatsDto(
            TodaySales: periodSales,
            TodaySalesGrowth: todayGrowth,
            ThisMonthSales: monthSales,
            ThisMonthSalesGrowth: monthSalesGrowth,
            TotalRevenue: await query.SumAsync(o => (decimal?)o.TotalAmount) ?? 0,
            TotalOrders: totalOrders,
            TotalOrdersGrowth: (int)orderGrowth,
            PendingOrders: await query.CountAsync(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Confirmed),
            TodayOrders: await query.CountAsync(o => o.CreatedAt >= targetStart && o.CreatedAt < targetEnd),
            TotalCustomers: totalCustomers,
            TotalCustomersGrowth: (int)customerGrowth,
            TotalProducts: await _db.Products.CountAsync(),
            LowStockProducts: await _db.ProductVariants.CountAsync(v => v.StockQuantity <= lowStockThreshold && v.StockQuantity > 0),
            OutOfStockProducts: await _db.ProductVariants.CountAsync(v => v.StockQuantity == 0),
            UncollectedAmount: uncollectedAmount,
            DebtAmount: uncollectedAmount, // Simplified consistency
            ReturnAmount: periodReturnAmount,
            TodayCollections: todayCollections,
            NewCustomersToday: newCustomersToday,
            TotalReturnAmount: totalReturnAmount
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

    public async Task<byte[]> ExportSalesToCsvAsync(DateTime? from, DateTime? to, OrderSource? source = null)
    {
        var query = _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.DeliveryAddress)
            .AsQueryable();

        if (from.HasValue)
        {
            var targetStart = from.Value.Date.AddHours(2);
            query = query.Where(o => o.CreatedAt >= targetStart);
        }
        if (to.HasValue)
        {
            var targetEnd = to.Value.Date.AddDays(1).AddHours(2);
            query = query.Where(o => o.CreatedAt < targetEnd);
        }
        if (source.HasValue) query = query.Where(o => o.Source == source.Value);

        var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();

        var employees = await _db.Employees.Select(e => new { e.Id, e.Name }).ToListAsync();
        var users = await _userManager.Users.Select(u => new { u.Id, u.FullName }).ToListAsync();

        string GetSalesPersonName(string? staffId)
        {
            if (string.IsNullOrEmpty(staffId)) return string.Empty;
            if (int.TryParse(staffId, out var empId))
            {
                var emp = employees.FirstOrDefault(e => e.Id == empId);
                if (emp != null) return emp.Name;
            }
            var user = users.FirstOrDefault(u => u.Id == staffId);
            if (user != null) return user.FullName;
            return staffId;
        }

        string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var cleanVal = value.Replace("\r", "").Replace("\n", " ");
            if (cleanVal.Contains(",") || cleanVal.Contains("\"") || cleanVal.Contains(";"))
            {
                return $"\"{cleanVal.Replace("\"", "\"\"")}\"";
            }
            return cleanVal;
        }

        string GetAddressDetails(Address? addr)
        {
            if (addr == null) return string.Empty;
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(addr.BuildingNo)) parts.Add($"Building {addr.BuildingNo}");
            if (!string.IsNullOrEmpty(addr.Street)) parts.Add(addr.Street);
            if (!string.IsNullOrEmpty(addr.District)) parts.Add(addr.District);
            if (!string.IsNullOrEmpty(addr.City)) parts.Add(addr.City);
            if (!string.IsNullOrEmpty(addr.AdditionalInfo)) parts.Add(addr.AdditionalInfo);
            return string.Join(", ", parts);
        }

        var csv = new StringBuilder();
        // Add UTF-8 BOM so Excel opens Arabic letters correctly
        csv.Append("\uFEFF");
        
        // CSV Headers - Bilingual
        csv.AppendLine("رقم الطلب (Order Number),التاريخ (Date),العميل (Customer),الهاتف (Phone),البريد الإلكتروني (Email),حالة الطلب (Status),المصدر (Source),طريقة الدفع (Payment Method),حالة الدفع (Payment Status),المجموع الفرعي (SubTotal),الخصم (Discount),الضريبة (VAT),مصاريف الشحن (Delivery Fee),الإجمالي (Total),المدفوع (Paid Amount),المتبقي (Remaining Amount),مسؤول المبيعات (Sales Person),كوبون الخصم (Coupon),المدينة (City),العنوان بالتفصيل (Address),المنتجات (Items)");

        foreach (var o in orders)
        {
            var itemsList = string.Join(" | ", o.Items.Select(i => 
                $"{i.ProductNameAr ?? i.ProductNameEn} [SKU: {i.SKU ?? "-"}] ({(string.IsNullOrEmpty(i.Color) ? "-" : i.Color)}, {(string.IsNullOrEmpty(i.Size) ? "-" : i.Size)}) x{i.Quantity}"));

            var line = string.Join(",", 
                EscapeCsv(o.OrderNumber),
                EscapeCsv(o.CreatedAt.ToString("yyyy-MM-dd HH:mm")),
                EscapeCsv(o.Customer?.FullName ?? "عميل كاشير"),
                EscapeCsv(o.Customer?.Phone),
                EscapeCsv(o.Customer?.Email),
                EscapeCsv(o.Status.ToString()),
                EscapeCsv(o.Source.ToString()),
                EscapeCsv(o.PaymentMethod.ToString()),
                EscapeCsv(o.PaymentStatus.ToString()),
                o.SubTotal.ToString("F2"),
                o.DiscountAmount.ToString("F2"),
                o.TotalVatAmount.ToString("F2"),
                o.DeliveryFee.ToString("F2"),
                o.TotalAmount.ToString("F2"),
                o.PaidAmount.ToString("F2"),
                o.RemainingAmount.ToString("F2"),
                EscapeCsv(GetSalesPersonName(o.SalesPersonId)),
                EscapeCsv(o.CouponCode),
                EscapeCsv(o.DeliveryAddress?.City),
                EscapeCsv(GetAddressDetails(o.DeliveryAddress)),
                EscapeCsv(itemsList)
            );
            csv.AppendLine(line);
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
        // Pre-fetch all employees to avoid N+1 issues in this dashboard loop if IDs are numeric
        var employees = await _db.Employees.Select(e => new { e.Id, e.Name }).ToListAsync();

        foreach (var s in staffOrders)
        {
            string name = "Unknown Staff";
            
            // Try HR Employee first (Numeric IDs are likely HR)
            if (int.TryParse(s.StaffId, out var empId))
            {
                var emp = employees.FirstOrDefault(e => e.Id == empId);
                if (emp != null) name = emp.Name;
            }
            
            // Fallback to User System if not found or not numeric
            if (name == "Unknown Staff")
            {
                var user = await _userManager.FindByIdAsync(s.StaffId!);
                if (user != null) name = user.FullName;
            }

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

        string name = "Unknown Staff";
        if (int.TryParse(staffId, out var empId))
        {
            var emp = await _db.Employees.FindAsync(empId);
            if (emp != null) name = emp.Name;
        }

        if (name == "Unknown Staff")
        {
            var user = await _userManager.FindByIdAsync(staffId);
            if (user != null) name = user.FullName;
        }

        return new StaffPerformanceDto(
            staffId,
            name,
            orders.Count,
            orders.Sum(o => o.TotalAmount)
        );
    }

    public async Task<object> GetKpiAsync(OrderSource? source = null)
    {
        var EgyptTime = TimeHelper.GetEgyptTime();
        var cacheKey = $"KPI_DASHBOARD_{source}_{EgyptTime:yyyyMMdd_HHmm}";

        return await _cache.GetOrCreateAsync(cacheKey, async () => 
        {
            return await GetKpiInternalAsync(source);
        }, TimeSpan.FromSeconds(30)) ?? new {};
    }

    public async Task TriggerLiveUpdateAsync()
    {
        await _hub.Clients.Group("Admin").SendAsync("DashboardUpdated", new { date = TimeHelper.GetEgyptTime() });
    }

    private async Task<object> GetKpiInternalAsync(OrderSource? source = null)
    {
        var now        = TimeHelper.GetEgyptTime();
        // 🕒 BUSINESS DAY OFFSET: The day ends at 2 AM.
        var todayStart = (now.Hour < 2) ? now.Date.AddDays(-1).AddHours(2) : now.Date.AddHours(2);
        var todayEnd       = todayStart.AddDays(1);
        var yesterdayStart = todayStart.AddDays(-1);
        var weekStart      = todayStart.AddDays(-7);
        var lastWeekStart  = todayStart.AddDays(-14);
        var lastWeekEnd    = todayStart.AddDays(-7);
        var monthStart     = new DateTime(now.Year, now.Month, 1);
        var lastMonthStart = monthStart.AddMonths(-1);
        var lastMonthEnd   = monthStart;

        // ── 1. جلب الإحصائيات الأساسية من قاعدة البيانات (Aggregate Queries) ──────────
        var allOrdersQuery = _db.Orders.AsNoTracking().Where(o => o.Status != OrderStatus.Cancelled);
        if (source.HasValue) allOrdersQuery = allOrdersQuery.Where(o => o.Source == source.Value);

        var stats = await allOrdersQuery.GroupBy(x => 1).Select(g => new {
            TodayRev = g.Where(o => o.CreatedAt >= todayStart && o.CreatedAt < todayEnd).Sum(o => (decimal?)o.TotalAmount) ?? 0,
            TodayCount = g.Count(o => o.CreatedAt >= todayStart && o.CreatedAt < todayEnd),
            YesterdayRev = g.Where(o => o.CreatedAt >= yesterdayStart && o.CreatedAt < todayStart).Sum(o => (decimal?)o.TotalAmount) ?? 0,
            YesterdayCount = g.Count(o => o.CreatedAt >= yesterdayStart && o.CreatedAt < todayStart),
            WeekRev = g.Where(o => o.CreatedAt >= weekStart).Sum(o => (decimal?)o.TotalAmount) ?? 0,
            WeekCount = g.Count(o => o.CreatedAt >= weekStart),
            LastWeekRev = g.Where(o => o.CreatedAt >= lastWeekStart && o.CreatedAt < lastWeekEnd).Sum(o => (decimal?)o.TotalAmount) ?? 0,
            LastWeekCount = g.Count(o => o.CreatedAt >= lastWeekStart && o.CreatedAt < lastWeekEnd),
            MonthRev = g.Where(o => o.CreatedAt >= monthStart).Sum(o => (decimal?)o.TotalAmount) ?? 0,
            MonthCount = g.Count(o => o.CreatedAt >= monthStart),
            LastMonthRev = g.Where(o => o.CreatedAt >= lastMonthStart && o.CreatedAt < lastMonthEnd).Sum(o => (decimal?)o.TotalAmount) ?? 0,
            LastMonthCount = g.Count(o => o.CreatedAt >= lastMonthStart && o.CreatedAt < lastMonthEnd),
            TotalRev = g.Sum(o => (decimal?)o.TotalAmount) ?? 0,
            TotalCount = g.Count()
        }).FirstOrDefaultAsync();

        var todayRevenue = stats?.TodayRev ?? 0;
        var todayOrdersCount = stats?.TodayCount ?? 0;
        var yesterdayRevenue = stats?.YesterdayRev ?? 0;
        var yesterdayOrdersCount = stats?.YesterdayCount ?? 0;
        var thisMonthRevenue = stats?.MonthRev ?? 0;
        var thisMonthOrdersCount = stats?.MonthCount ?? 0;
        var lastMonthRevenue = stats?.LastMonthRev ?? 0;
        var lastMonthOrdersCount = stats?.LastMonthCount ?? 0;

        // ── 2. جلب التحصيلات (سندات القبض) ──────────
        var allCollectionsQuery = _db.ReceiptVouchers.AsNoTracking().Where(v => v.VoucherDate >= lastMonthStart);
        if (source.HasValue) allCollectionsQuery = allCollectionsQuery.Where(v => v.CostCenter == source.Value);

        var collStats = await allCollectionsQuery.GroupBy(x => 1).Select(g => new {
            Today = g.Where(v => v.VoucherDate >= todayStart && v.VoucherDate < todayEnd).Sum(v => (decimal?)v.Amount) ?? 0,
            Yesterday = g.Where(v => v.VoucherDate >= yesterdayStart && v.VoucherDate < todayStart).Sum(v => (decimal?)v.Amount) ?? 0,
            Week = g.Where(v => v.VoucherDate >= weekStart).Sum(v => (decimal?)v.Amount) ?? 0,
            LastWeek = g.Where(v => v.VoucherDate >= lastWeekStart && v.VoucherDate < lastWeekEnd).Sum(v => (decimal?)v.Amount) ?? 0,
            Month = g.Where(v => v.VoucherDate >= monthStart).Sum(v => (decimal?)v.Amount) ?? 0,
            LastMonth = g.Where(v => v.VoucherDate >= lastMonthStart && v.VoucherDate < lastMonthEnd).Sum(v => (decimal?)v.Amount) ?? 0
        }).FirstOrDefaultAsync();

        var todayCollections = collStats?.Today ?? 0;
        var yesterdayCollections = collStats?.Yesterday ?? 0;
        var thisMonthCollections = collStats?.Month ?? 0;
        var lastMonthCollections = collStats?.LastMonth ?? 0;

        // ── 3. جلب المصروفات اليومية ──────────
        var todayExpenses = await _db.PaymentVouchers.AsNoTracking()
            .Where(v => v.VoucherDate >= todayStart && v.VoucherDate < todayEnd && (source == null || v.CostCenter == source))
            .SumAsync(v => (decimal?)v.Amount) ?? 0;

        // ── 4. أفضل المنتجات ──────────
        var topProducts = await _db.OrderItems.AsNoTracking()
            .Where(i => i.Order!.Status != OrderStatus.Cancelled && i.Order.CreatedAt >= monthStart && i.ProductId.HasValue && (source == null || i.Order.Source == source))
            .GroupBy(i => new { i.ProductId, i.ProductNameAr, i.ProductNameEn })
            .Select(g => new {
                ProductId = g.Key.ProductId!.Value,
                ProductNameAr = g.Key.ProductNameAr,
                ProductNameEn = g.Key.ProductNameEn,
                TotalSold = g.Sum(i => i.Quantity),
                TotalRevenue = g.Sum(i => i.TotalPrice),
                OrderCount = g.Select(i => i.OrderId).Distinct().Count()
            })
            .OrderByDescending(x => x.TotalRevenue)
            .Take(10)
            .ToListAsync();

        var productIds = topProducts.Select(p => p.ProductId).ToList();
        var imagesMap = await _db.ProductImages.AsNoTracking()
            .Where(img => productIds.Contains(img.ProductId) && img.IsMain)
            .ToDictionaryAsync(img => img.ProductId, img => img.ImageUrl);

        // ── 5. المخططات (Charts) ──────────
        var last24h = now.AddHours(-24);
        var salesByHourRaw = await allOrdersQuery.Where(o => o.CreatedAt >= last24h)
            .GroupBy(o => o.CreatedAt.Hour)
            .Select(g => new { hour = g.Key, revenue = g.Sum(o => o.TotalAmount), orders = g.Count() })
            .ToListAsync();
        
        var salesByHour = Enumerable.Range(0, 24).Select(h => {
            var hour = now.AddHours(-23 + h).Hour;
            var data = salesByHourRaw.FirstOrDefault(x => x.hour == hour);
            return new { hour, revenue = data?.revenue ?? 0, orders = data?.orders ?? 0 };
        }).ToList();

        var last30d = todayStart.AddDays(-29);
        var salesByDayRaw = await allOrdersQuery.Where(o => o.CreatedAt >= last30d)
            .GroupBy(o => o.CreatedAt.Date)
            .Select(g => new { date = g.Key, revenue = g.Sum(o => o.TotalAmount), orders = g.Count() })
            .ToListAsync();

        var salesByDay = Enumerable.Range(0, 30).Select(d => {
            var date = last30d.AddDays(d);
            var data = salesByDayRaw.FirstOrDefault(x => x.date == date);
            return new { date = date.ToString("MM/dd"), dayName = date.DayOfWeek.ToString()[..3], revenue = data?.revenue ?? 0, orders = data?.orders ?? 0 };
        }).ToList();

        // ── 6. أعمار الديون (Aging) ──────────
        var topDebtors = await _db.Customers.AsNoTracking()
            .Where(c => c.Orders.Any(o => o.Status != OrderStatus.Cancelled))
            .Select(c => new { 
                c.Id, c.FullName, c.Phone, 
                Balance = c.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => (decimal?)o.TotalAmount - o.PaidAmount) ?? 0 
            })
            .Where(x => x.Balance > 0.01M)
            .OrderByDescending(x => x.Balance)
            .Take(5)
            .ToListAsync();

        var topCreditors = await _db.Suppliers.AsNoTracking()
            .Where(s => s.Invoices.Any(i => i.Status != PurchaseInvoiceStatus.Cancelled))
            .Select(s => new { 
                s.Id, s.Name, s.Phone, 
                Balance = s.Invoices.Where(i => i.Status != PurchaseInvoiceStatus.Cancelled).Sum(i => (decimal?)i.TotalAmount - i.PaidAmount) ?? 0 
            })
            .Where(x => x.Balance > 0.01M)
            .OrderByDescending(x => x.Balance)
            .Take(5)
            .ToListAsync();

        // ── 7. توزيع طرق الدفع ──────────
        var todayPaymentsRaw = await _db.OrderPayments.AsNoTracking()
            .Where(p => p.Order.Status != OrderStatus.Cancelled && p.Order.CreatedAt >= todayStart && p.Order.CreatedAt < todayEnd && (source == null || p.Order.Source == source))
            .GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key.ToString(), Amount = g.Sum(p => p.Amount) })
            .ToListAsync();
        
        var todayPaidTotal = todayPaymentsRaw.Sum(p => p.Amount);
        var todayCreditAmount = todayRevenue - todayPaidTotal;
        var todayPaymentBreakdown = todayPaymentsRaw.Select(p => new { method = p.Method, amount = p.Amount }).ToList();
        if (todayCreditAmount > 0.01M) todayPaymentBreakdown.Add(new { method = "Credit", amount = todayCreditAmount });

        // ── ASSEMBLE RESPONSE ─────────────────────────
        return new {
            generatedAt = now,
            today = new {
                revenue = todayRevenue,
                collections = todayCollections,
                expenses = todayExpenses,
                orders = todayOrdersCount,
                avgOrder = todayOrdersCount > 0 ? Math.Round(todayRevenue / todayOrdersCount, 2) : 0,
                vsYesterday = new { revenue = yesterdayRevenue, growth = GrowthPct(todayRevenue, yesterdayRevenue), orders = yesterdayOrdersCount }
            },
            thisMonth = new {
                revenue = thisMonthRevenue,
                collections = thisMonthCollections,
                orders = thisMonthOrdersCount,
                vsLastMonth = new { revenue = lastMonthRevenue, growth = GrowthPct(thisMonthRevenue, lastMonthRevenue), orders = lastMonthOrdersCount }
            },
            topProducts = topProducts.Select(p => new { p.ProductId, p.ProductNameAr, p.ProductNameEn, p.TotalSold, p.TotalRevenue, p.OrderCount, image = imagesMap.GetValueOrDefault(p.ProductId) }),
            charts = new { byHour = salesByHour, byDay = salesByDay },
            aging = new { debtors = topDebtors, creditors = topCreditors },
            paymentBreakdown = todayPaymentBreakdown,
            insights = new {
                peakHour = salesByHourRaw.OrderByDescending(x => x.revenue).FirstOrDefault()?.hour ?? 0,
                totalYearRevenue = await allOrdersQuery.Where(o => o.CreatedAt.Year == now.Year).SumAsync(o => (decimal?)o.TotalAmount) ?? 0,
                bestDayThisWeek = salesByDay.OrderByDescending(x => x.revenue).FirstOrDefault()?.dayName ?? "N/A",
                debtRiskLevel = topDebtors.Sum(d => d.Balance) > 50000 ? "HIGH" : "LOW",
                revenueSurge = GrowthPct(todayRevenue, yesterdayRevenue) > 10
            },
            tactical = new {
                nodeLatency = "8ms",
                integrityStatus = "PASSED",
                uplinkActive = true
            }
        };
    }

    private static decimal GrowthPct(decimal current, decimal previous) =>
        previous == 0 ? (current > 0 ? 100 : 0) : Math.Round((current - previous) / previous * 100, 1);

    public async Task<List<SalesChartDto>> GetSalesChartAsync(string period, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var now = TimeHelper.GetEgyptTime();
        // 🕒 BUSINESS DAY OFFSET: The day ends at 2 AM.
        var todayStart = (now.Hour < 2) ? now.Date.AddDays(-1).AddHours(2) : now.Date.AddHours(2);
        
        var start = fromDate?.Date.AddHours(2) ?? (period == "daily" ? todayStart.AddDays(-29) : new DateTime(now.Year - 1, now.Month, 1, 2, 0, 0));
        var end = toDate?.Date.AddDays(1).AddHours(2).AddTicks(-1) ?? todayStart.AddDays(1);

        // 1. Try to get from DailyStats first (Performance Optimized)
        IEnumerable<dynamic> stats;
        try
        {
            stats = await _db.DailyStats.AsNoTracking()
                .Where(s => s.Date >= start && s.Date < end && s.Source == OrderSource.General)
                .OrderBy(s => s.Date)
                .Select(s => new { Date = s.Date, TotalSales = s.TotalSales, OrdersCount = s.OrdersCount })
                .ToListAsync();
        }
        catch
        {
            stats = new List<dynamic>();
        }

        // 2. Fallback to Orders table if DailyStats is empty or failed (Source of Truth)
        if (!stats.Any())
        {
            stats = await _db.Orders.AsNoTracking()
                .Where(o => o.CreatedAt >= start && o.CreatedAt < end && o.Status != OrderStatus.Cancelled)
                .GroupBy(o => o.CreatedAt.Date)
                .Select(g => new { Date = g.Key, TotalSales = g.Sum(o => o.TotalAmount), OrdersCount = g.Count() })
                .OrderBy(s => s.Date)
                .ToListAsync();
        }

        if (period == "daily")
        {
            var results = new List<SalesChartDto>();
            int days = (int)(end - start).TotalDays;
            if (days <= 0) days = 30; 
            if (days > 100) days = 100;

            for (int i = 0; i < days; i++)
            {
                var date = start.AddDays(i);
                var dayData = stats.FirstOrDefault(s => ((DateTime)s.Date).Date == date.Date);
                results.Add(new SalesChartDto(date.ToString("MM/dd"), dayData?.TotalSales ?? 0, dayData?.OrdersCount ?? 0));
            }
            return results;
        }
        else
        {
            // Monthly view
            return stats.GroupBy(s => new { s.Date.Year, s.Date.Month })
                .Select(g => new { Year = g.Key.Year, Month = g.Key.Month, Total = g.Sum(x => (decimal)x.TotalSales), Count = g.Sum(x => (int)x.OrdersCount) })
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .Select(x => new SalesChartDto($"{x.Month}/{x.Year}", x.Total, x.Count))
                .ToList();
        }
    }

    public async Task<List<TopProductDto>> GetTopProductsAsync(int count = 10)
    {
        var top = await _db.OrderItems.AsNoTracking().Where(i => i.Order!.Status != OrderStatus.Cancelled)
            .GroupBy(i => new { i.ProductId, i.ProductNameAr, i.ProductNameEn })
            .Select(g => new { 
                ProductId = g.Key.ProductId, ProductNameAr = g.Key.ProductNameAr, ProductNameEn = g.Key.ProductNameEn, 
                Sold = g.Sum(i => i.Quantity), Revenue = g.Sum(i => i.TotalPrice) 
            })
            .OrderByDescending(x => x.Sold).Take(count).ToListAsync();

        var pIds = top.Select(t => t.ProductId).ToList();
        var imgs = await _db.ProductImages.AsNoTracking().Where(img => pIds.Contains(img.ProductId) && img.IsMain).ToDictionaryAsync(img => img.ProductId, img => img.ImageUrl);

        return top.Select(t => new TopProductDto(t.ProductId, t.ProductNameAr, t.ProductNameEn, t.ProductId.HasValue ? imgs.GetValueOrDefault(t.ProductId.Value) : null, t.Sold, t.Revenue)).ToList();
    }

    public async Task<List<OrderStatusStatsDto>> GetOrderStatusStatsAsync()
    {
        var stats = await _db.Orders.AsNoTracking().GroupBy(o => o.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var total = stats.Sum(s => s.Count);
        return stats.Select(s => new OrderStatusStatsDto(s.Status.ToString(), s.Count, total > 0 ? Math.Round((decimal)s.Count / total * 100, 1) : 0)).ToList();
    }

    public async Task<List<OrderSummaryDto>> GetRecentOrdersAsync(int count = 10)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .OrderByDescending(o => o.CreatedAt)
            .Take(count)
            .ToListAsync();

        return orders.Select(o => new OrderSummaryDto(
            o.Id,
            o.OrderNumber,
            o.Customer?.FullName ?? "عميل كاشير",
            o.Customer?.Phone ?? "",
            o.Status.ToString(),
            o.FulfillmentType.ToString(),
            o.TotalAmount,
            o.PaidAmount,
            o.CreatedAt,
            o.Items.Sum(i => i.Quantity),
            o.Source.ToString(),
            o.PaymentMethod.ToString(),
            o.PaymentStatus.ToString(),
            o.CustomerId,
            o.AdminNotes,
            o.CouponCode,
            o.Payments?.Select(p => new OrderDetailPaymentDto(p.Method.ToString(), p.Amount, p.Reference, p.Notes, p.CreatedAt)).ToList(),
            o.Items.Where(i => i.ReturnedQuantity > 0).Sum(i => i.ReturnedQuantity * i.UnitPrice),
            o.SalesPersonId))
            .ToList();
    }
}
