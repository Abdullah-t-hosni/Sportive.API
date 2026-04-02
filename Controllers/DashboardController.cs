using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboard;
    public DashboardController(IDashboardService dashboard) => _dashboard = dashboard;

    [HttpGet("stats")]
    [HttpGet("/api/analytics/admin-stats")] // Compatibility for old frontend
    public async Task<IActionResult> GetStats() =>
        Ok(await _dashboard.GetStatsAsync());

    // ✅ kpi endpoint moved to DashboardKpiController (api/dashboard/kpi)
    // Removed duplicate [HttpGet("kpi")] that was causing Swagger 500 error

    [HttpGet("sales-chart")]
    public async Task<IActionResult> GetSalesChart([FromQuery] string period = "monthly") =>
        Ok(await _dashboard.GetSalesChartAsync(period));

    [HttpGet("top-products")]
    public async Task<IActionResult> GetTopProducts([FromQuery] int count = 10) =>
        Ok(await _dashboard.GetTopProductsAsync(count));

    [HttpGet("order-status-stats")]
    public async Task<IActionResult> GetOrderStatusStats() =>
        Ok(await _dashboard.GetOrderStatusStatsAsync());

    [HttpGet("recent-orders")]
    public async Task<IActionResult> GetRecentOrders([FromQuery] int count = 10) =>
        Ok(await _dashboard.GetRecentOrdersAsync(count));

    [HttpGet("analytics-summary")]
    [HttpGet("/api/analytics/summary")] // Compatibility for old frontend
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

    [HttpPost("trigger-update")]
    public async Task<IActionResult> TriggerUpdate()
    {
        await _dashboard.TriggerLiveUpdateAsync();
        return Ok();
    }
}
