using Sportive.API.DTOs;
using Sportive.API.Models;

namespace Sportive.API.Interfaces;

public interface IDashboardService
{
    Task<DashboardStatsDto> GetStatsAsync(OrderSource? source = null, DateTime? fromDate = null, DateTime? toDate = null);
    Task<List<SalesChartDto>> GetSalesChartAsync(string period, DateTime? fromDate = null, DateTime? toDate = null);
    Task<List<TopProductDto>> GetTopProductsAsync(int count = 10);
    Task<List<OrderStatusStatsDto>> GetOrderStatusStatsAsync();
    Task<List<OrderSummaryDto>> GetRecentOrdersAsync(int count = 10);
    Task<AnalyticsSummaryDto> GetAnalyticsSummaryAsync();
    Task<byte[]> ExportSalesToCsvAsync(DateTime? from, DateTime? to, OrderSource? source = null);
    Task<AdvancedDashboardStatsDto> GetAdvancedStatsAsync();
    Task<StaffPerformanceDto> GetStaffStatsAsync(string staffId);
    Task<object> GetKpiAsync(OrderSource? source = null);
    Task TriggerLiveUpdateAsync();
}
