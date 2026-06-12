using Sportive.API.DTOs;
using Sportive.API.Models;

namespace Sportive.API.Interfaces;

public interface IDashboardService
{
    Task<DashboardStatsDto> GetStatsAsync(OrderSource? source = null, DateTime? fromDate = null, DateTime? toDate = null, int? branchId = null);
    Task<List<SalesChartDto>> GetSalesChartAsync(string period, DateTime? fromDate = null, DateTime? toDate = null, int? branchId = null);
    Task<List<TopProductDto>> GetTopProductsAsync(int count = 10, int? branchId = null);
    Task<List<OrderStatusStatsDto>> GetOrderStatusStatsAsync(int? branchId = null);
    Task<List<OrderSummaryDto>> GetRecentOrdersAsync(int count = 10, int? branchId = null);
    Task<AnalyticsSummaryDto> GetAnalyticsSummaryAsync(int? branchId = null);
    Task<byte[]> ExportSalesToCsvAsync(DateTime? from, DateTime? to, OrderSource? source = null, int? branchId = null);
    Task<AdvancedDashboardStatsDto> GetAdvancedStatsAsync(int? branchId = null);
    Task<StaffPerformanceDto> GetStaffStatsAsync(string staffId, int? branchId = null);
    Task<object> GetKpiAsync(OrderSource? source = null, DateTime? fromDate = null, DateTime? toDate = null, int? branchId = null);
    Task TriggerLiveUpdateAsync();
}
