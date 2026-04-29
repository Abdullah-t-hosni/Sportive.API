using Sportive.API.Attributes;
using Sportive.API.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

/// <summary>
/// GET /api/dashboard/kpi
/// Ù…Ø¤Ø´Ø±Ø§Øª KPI Ù…ØªÙ‚Ø¯Ù…Ø© â€” Ø¯Ø§Ø´Ø¨ÙˆØ±Ø¯ Ø´Ø§Ù…Ù„
/// </summary>
[ApiController]
[Route("api/dashboard")]
[RequirePermission(ModuleKeys.Dashboard)]
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
    public async Task<IActionResult> GetStats([FromQuery] OrderSource? source = null, [FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null) =>
        Ok(await _dashboard.GetStatsAsync(source, fromDate, toDate));

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
        return File(csvBytes, "text/csv", $"sales-report-{TimeHelper.GetEgyptTime():yyyyMMdd}.csv");
    }

    [HttpGet("advanced-stats")]
    public async Task<IActionResult> GetAdvancedStats() =>
        Ok(await _dashboard.GetAdvancedStatsAsync());

    [HttpGet("staff-stats")]
    public async Task<IActionResult> GetStaffStats([FromQuery] string staffId) =>
        Ok(await _dashboard.GetStaffStatsAsync(staffId));

    // âœ… Compatibility Aliases
    [HttpGet("/api/analytics/admin-stats")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> GetStatsAlias([FromQuery] OrderSource? source = null) =>
        Ok(await _dashboard.GetStatsAsync(source));

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
    public async Task<IActionResult> GetKpi([FromQuery] OrderSource? source = null)
    {
        return Ok(await _dashboard.GetKpiAsync(source));
    }

    private static decimal GrowthPct(decimal current, decimal previous) =>
        previous == 0 ? (current > 0 ? 100 : 0) :
        Math.Round((current - previous) / previous * 100, 1);
}

