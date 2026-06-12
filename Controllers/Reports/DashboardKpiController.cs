using Sportive.API.Attributes;
using Sportive.API.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Interfaces;
using Sportive.API.Extensions;

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
    public async Task<IActionResult> GetStats([FromQuery] OrderSource? source = null, [FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : null;
        return Ok(await _dashboard.GetStatsAsync(source, fromDate, toDate, branchId));
    }

    [HttpGet("sales-chart")]
    public async Task<IActionResult> GetSalesChart([FromQuery] string period = "monthly", [FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : null;
        return Ok(await _dashboard.GetSalesChartAsync(period, fromDate, toDate, branchId));
    }

    [HttpGet("top-products")]
    public async Task<IActionResult> GetTopProductsList([FromQuery] int count = 10)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : null;
        return Ok(await _dashboard.GetTopProductsAsync(count, branchId));
    }

    [HttpGet("order-status-stats")]
    public async Task<IActionResult> GetOrderStatusStats()
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : null;
        return Ok(await _dashboard.GetOrderStatusStatsAsync(branchId));
    }

    [HttpGet("recent-orders")]
    public async Task<IActionResult> GetRecentOrders([FromQuery] int count = 10)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : null;
        return Ok(await _dashboard.GetRecentOrdersAsync(count, branchId));
    }

    [HttpGet("analytics-summary")]
    public async Task<IActionResult> GetAnalyticsSummary()
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : null;
        return Ok(await _dashboard.GetAnalyticsSummaryAsync(branchId));
    }

    [HttpGet("export-sales")]
    public async Task<IActionResult> ExportSales([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] OrderSource? source = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : null;
        var csvBytes = await _dashboard.ExportSalesToCsvAsync(from, to, source, branchId);
        return File(csvBytes, "text/csv", $"sales-report-{TimeHelper.GetEgyptTime():yyyyMMdd}.csv");
    }

    [HttpGet("advanced-stats")]
    public async Task<IActionResult> GetAdvancedStats()
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : null;
        return Ok(await _dashboard.GetAdvancedStatsAsync(branchId));
    }

    [HttpGet("staff-stats")]
    public async Task<IActionResult> GetStaffStats([FromQuery] string staffId)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : null;
        return Ok(await _dashboard.GetStaffStatsAsync(staffId, branchId));
    }

    // âœ… Compatibility Aliases
    [HttpGet("/api/analytics/admin-stats")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> GetStatsAlias([FromQuery] OrderSource? source = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : null;
        return Ok(await _dashboard.GetStatsAsync(source, null, null, branchId));
    }

    [HttpGet("/api/analytics/summary")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> GetAnalyticsSummaryAlias()
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : null;
        return Ok(await _dashboard.GetAnalyticsSummaryAsync(branchId));
    }

    [HttpPost("trigger-update")]
    public async Task<IActionResult> TriggerUpdate()
    {
        await _dashboard.TriggerLiveUpdateAsync();
        return Ok();
    }

    [HttpGet("kpi")]
    public async Task<IActionResult> GetKpi(
        [FromQuery] OrderSource? source = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : null;
        return Ok(await _dashboard.GetKpiAsync(source, fromDate, toDate, branchId));
    }

    private static decimal GrowthPct(decimal current, decimal previous) =>
        previous == 0 ? (current > 0 ? 100 : 0) :
        Math.Round((current - previous) / previous * 100, 1);
}

