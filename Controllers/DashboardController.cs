using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboard;
    public DashboardController(IDashboardService dashboard) => _dashboard = dashboard;

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats() =>
        Ok(await _dashboard.GetStatsAsync());

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
    public async Task<IActionResult> GetAnalyticsSummary() =>
        Ok(await _dashboard.GetAnalyticsSummaryAsync());

    [HttpGet("export-sales")]
    public async Task<IActionResult> ExportSales([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var csvBytes = await _dashboard.ExportSalesToCsvAsync(from, to);
        return File(csvBytes, "text/csv", $"sales-report-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpPost("trigger-update")]
    public async Task<IActionResult> TriggerUpdate()
    {
        await _dashboard.TriggerLiveUpdateAsync();
        return Ok();
    }
}
