// ============================================================
// DTOs/DashboardDtos.cs
// تم الفصل من Dtos.cs الكبير — يشمل Dashboard, Analytics, KPIs
// ============================================================
namespace Sportive.API.DTOs;

// ========== DASHBOARD ==========
public record DashboardStatsDto(
    decimal TodaySales,
    decimal TodaySalesGrowth,
    decimal ThisMonthSales,
    decimal ThisMonthSalesGrowth,
    decimal TotalRevenue,
    int TotalOrders,
    int TotalOrdersGrowth,
    int PendingOrders,
    int TodayOrders,
    int TotalCustomers,
    int TotalCustomersGrowth,
    int TotalProducts,
    int LowStockProducts,
    int OutOfStockProducts,
    decimal UncollectedAmount = 0,
    decimal DebtAmount = 0,
    decimal ReturnAmount = 0
);

public record AnalyticsSummaryDto(
    List<CategorySalesDto> CategorySales,
    List<TopProductDto> TopSellingProducts,
    List<DailyMetricDto> DailySales,
    decimal AverageOrderValue,
    decimal CustomerRetentionRate
);

public record CategorySalesDto(int CategoryId, string NameAr, string NameEn, int TotalSold, decimal TotalRevenue);
public record DailyMetricDto(DateTime Date, decimal Revenue, int Orders, int NewCustomers);
public record SalesChartDto(string Label, decimal Amount, int OrderCount);

public record TopProductDto(
    int? ProductId,
    string NameAr,
    string NameEn,
    string? ImageUrl,
    int TotalSold,
    decimal TotalRevenue
);

public record OrderStatusStatsDto(string Status, int Count, decimal Percentage);

public record AdvancedDashboardStatsDto(
    List<LocationStatDto> SalesByCity,
    List<VipCustomerDto> TopCustomers,
    List<InventoryIntelligenceDto> InventoryInsights,
    AbandonedCartDto AbandonedCarts,
    List<PaymentMethodStatDto> PaymentMethods,
    List<AdminActivityDto> RecentActivity,
    List<StaffPerformanceDto> StaffPerformance
);

public record StaffPerformanceDto(string StaffId, string StaffName, int OrderCount, decimal TotalSales);
public record LocationStatDto(string Name, int OrderCount, decimal TotalRevenue);
public record VipCustomerDto(int Id, string Name, string Email, decimal TotalSpent, int OrderCount);
public record InventoryIntelligenceDto(int? ProductId, string Name, int Stock, double AvgDailySales, int? DaysRemaining);
public record AbandonedCartDto(int Count, int TotalItems, decimal PotentialRevenue);
public record PaymentMethodStatDto(string Method, int Count, decimal Revenue);
public record AdminActivityDto(string AdminName, string Action, string Target, DateTime Date);

// ========== PAGINATION ==========
public record PaginatedResult<T>(
    List<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);

// ========== NOTIFICATIONS ==========
public record SendNotificationDto(
    string TitleAr,
    string TitleEn,
    string MessageAr,
    string MessageEn,
    string Type,
    string? ActionUrl = null,
    int? CustomerId = null
);
