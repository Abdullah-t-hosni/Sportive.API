using Sportive.API.Attributes;
using Sportive.API.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Interfaces;
using Sportive.API.Extensions;
using Sportive.API.Services;

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
    public async Task<IActionResult> GetStats([FromQuery] OrderSource? source = null, [FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null, [FromQuery] int? filterBranchId = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : (filterBranchId > 0 ? filterBranchId : null);
        return Ok(await _dashboard.GetStatsAsync(source, fromDate, toDate, branchId));
    }

    [HttpGet("sales-chart")]
    public async Task<IActionResult> GetSalesChart([FromQuery] string period = "monthly", [FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null, [FromQuery] int? filterBranchId = null, [FromQuery] OrderSource? source = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : (filterBranchId > 0 ? filterBranchId : null);
        return Ok(await _dashboard.GetSalesChartAsync(period, fromDate, toDate, branchId, source));
    }

    [HttpGet("top-products")]
    public async Task<IActionResult> GetTopProductsList([FromQuery] int count = 10, [FromQuery] int? filterBranchId = null, [FromQuery] OrderSource? source = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : (filterBranchId > 0 ? filterBranchId : null);
        return Ok(await _dashboard.GetTopProductsAsync(count, branchId, source));
    }

    [HttpGet("order-status-stats")]
    public async Task<IActionResult> GetOrderStatusStats([FromQuery] int? filterBranchId = null, [FromQuery] OrderSource? source = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : (filterBranchId > 0 ? filterBranchId : null);
        return Ok(await _dashboard.GetOrderStatusStatsAsync(branchId, source));
    }

    [HttpGet("recent-orders")]
    public async Task<IActionResult> GetRecentOrders([FromQuery] int count = 10, [FromQuery] int? filterBranchId = null, [FromQuery] OrderSource? source = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : (filterBranchId > 0 ? filterBranchId : null);
        return Ok(await _dashboard.GetRecentOrdersAsync(count, branchId, source));
    }

    [HttpGet("analytics-summary")]
    public async Task<IActionResult> GetAnalyticsSummary([FromQuery] int? filterBranchId = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : (filterBranchId > 0 ? filterBranchId : null);
        return Ok(await _dashboard.GetAnalyticsSummaryAsync(branchId));
    }

    [HttpGet("export-sales")]
    public async Task<IActionResult> ExportSales([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] OrderSource? source = null, [FromQuery] int? filterBranchId = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : (filterBranchId > 0 ? filterBranchId : null);
        var csvBytes = await _dashboard.ExportSalesToCsvAsync(from, to, source, branchId);
        return File(csvBytes, "text/csv", $"sales-report-{TimeHelper.GetEgyptTime():yyyyMMdd}.csv");
    }

    [HttpGet("advanced-stats")]
    public async Task<IActionResult> GetAdvancedStats([FromQuery] int? filterBranchId = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : (filterBranchId > 0 ? filterBranchId : null);
        return Ok(await _dashboard.GetAdvancedStatsAsync(branchId));
    }

    [HttpGet("staff-stats")]
    public async Task<IActionResult> GetStaffStats([FromQuery] string staffId, [FromQuery] int? filterBranchId = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : (filterBranchId > 0 ? filterBranchId : null);
        return Ok(await _dashboard.GetStaffStatsAsync(staffId, branchId));
    }

    // ✅ Compatibility Aliases
    [HttpGet("/api/analytics/admin-stats")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> GetStatsAlias([FromQuery] OrderSource? source = null, [FromQuery] int? filterBranchId = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : (filterBranchId > 0 ? filterBranchId : null);
        return Ok(await _dashboard.GetStatsAsync(source, null, null, branchId));
    }

    [HttpGet("/api/analytics/summary")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> GetAnalyticsSummaryAlias([FromQuery] int? filterBranchId = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : (filterBranchId > 0 ? filterBranchId : null);
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
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int? filterBranchId = null)
    {
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? branchId = !canViewAll ? User.GetBranchId() : (filterBranchId > 0 ? filterBranchId : null);
        return Ok(await _dashboard.GetKpiAsync(source, fromDate, toDate, branchId));
    }

    [HttpGet("store-visitors")]
    public async Task<IActionResult> GetStoreVisitors(
        [FromServices] IGoogleAnalyticsService ga4Service,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        return Ok(await ga4Service.GetStoreVisitorsStatsAsync(startDate, endDate));
    }

    [HttpGet("store-visitors/export")]
    public async Task<IActionResult> GetStoreVisitorsExport(
        [FromServices] IGoogleAnalyticsService ga4Service,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var data = await ga4Service.GetStoreVisitorsStatsAsync(startDate, endDate);
        // data is an anonymous object, so we'll serialize/deserialize to a known structure or use reflection.
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        var stats = System.Text.Json.JsonDocument.Parse(json).RootElement;

        using var workbook = new ClosedXML.Excel.XLWorkbook();
        
        // --- Sheet 1: Overview ---
        var ws1 = workbook.Worksheets.Add("نظرة عامة");
        ws1.RightToLeft = true;
        
        // Styling Header
        ws1.Range("A1:D1").Merge().Value = "تقرير تحليلات المتجر (Overview)";
        ws1.Range("A1:D1").Style.Font.Bold = true;
        ws1.Range("A1:D1").Style.Font.FontSize = 16;
        ws1.Range("A1:D1").Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
        ws1.Range("A1:D1").Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#4F46E5"); // Indigo-600
        ws1.Range("A1:D1").Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

        ws1.Cell("A3").Value = "الزوار النشطين الآن";
        ws1.Cell("B3").Value = stats.GetProperty("activeUsers").GetInt32();
        
        ws1.Cell("A4").Value = "متوسط مدة الجلسة";
        ws1.Cell("B4").Value = stats.GetProperty("sessionDuration").GetString();
        
        ws1.Cell("A5").Value = "معدل الارتداد";
        ws1.Cell("B5").Value = stats.GetProperty("bounceRate").GetString();

        ws1.Cell("A6").Value = "الجوال";
        ws1.Cell("B6").Value = stats.GetProperty("devices").GetProperty("mobile").GetString();
        
        ws1.Cell("A7").Value = "الكمبيوتر";
        ws1.Cell("B7").Value = stats.GetProperty("devices").GetProperty("desktop").GetString();

        ws1.Range("A3:A7").Style.Font.Bold = true;
        ws1.Range("A3:A7").Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#EEF2FF"); // Indigo-50
        ws1.Columns().AdjustToContents();

        // --- Sheet 2: Top Pages & Products ---
        var ws2 = workbook.Worksheets.Add("الصفحات والمنتجات");
        ws2.RightToLeft = true;

        ws2.Cell("A1").Value = "الصفحات الأكثر زيارة";
        ws2.Range("A1:C1").Merge();
        ws2.Range("A1:C1").Style.Font.Bold = true;
        ws2.Range("A1:C1").Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#D97706"); // Amber-600
        ws2.Range("A1:C1").Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

        ws2.Cell("A2").Value = "الصفحة";
        ws2.Cell("B2").Value = "الرابط";
        ws2.Cell("C2").Value = "المشاهدات";
        ws2.Range("A2:C2").Style.Font.Bold = true;
        ws2.Range("A2:C2").Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#FEF3C7"); // Amber-50

        int row = 3;
        foreach (var page in stats.GetProperty("topPages").EnumerateArray())
        {
            ws2.Cell(row, 1).Value = page.GetProperty("title").GetString();
            ws2.Cell(row, 2).Value = page.GetProperty("path").GetString();
            ws2.Cell(row, 3).Value = page.GetProperty("views").GetInt64();
            row++;
        }

        row += 2;
        ws2.Cell(row, 1).Value = "المنتجات الأكثر مشاهدة";
        ws2.Range(row, 1, row, 2).Merge();
        ws2.Range(row, 1, row, 2).Style.Font.Bold = true;
        ws2.Range(row, 1, row, 2).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#4F46E5"); // Indigo-600
        ws2.Range(row, 1, row, 2).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

        row++;
        ws2.Cell(row, 1).Value = "المنتج";
        ws2.Cell(row, 2).Value = "المشاهدات";
        ws2.Range(row, 1, row, 2).Style.Font.Bold = true;
        ws2.Range(row, 1, row, 2).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#EEF2FF");

        row++;
        foreach (var prod in stats.GetProperty("topProducts").EnumerateArray())
        {
            ws2.Cell(row, 1).Value = prod.GetProperty("name").GetString();
            ws2.Cell(row, 2).Value = prod.GetProperty("views").GetInt64();
            row++;
        }
        ws2.Columns().AdjustToContents();

        // --- Sheet 3: Demographics ---
        var ws3 = workbook.Worksheets.Add("المواقع الجغرافية");
        ws3.RightToLeft = true;

        ws3.Cell("A1").Value = "الدول";
        ws3.Cell("B1").Value = "النسبة";
        ws3.Range("A1:B1").Style.Font.Bold = true;
        ws3.Range("A1:B1").Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#4F46E5");
        ws3.Range("A1:B1").Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

        row = 2;
        foreach (var country in stats.GetProperty("countries").EnumerateArray())
        {
            ws3.Cell(row, 1).Value = country.GetProperty("name").GetString();
            ws3.Cell(row, 2).Value = country.GetProperty("percent").GetString();
            row++;
        }

        row += 2;
        ws3.Cell(row, 1).Value = "المدن";
        ws3.Cell(row, 2).Value = "المستخدمين";
        ws3.Range(row, 1, row, 2).Style.Font.Bold = true;
        ws3.Range(row, 1, row, 2).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#D97706");
        ws3.Range(row, 1, row, 2).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

        row++;
        foreach (var city in stats.GetProperty("cities").EnumerateArray())
        {
            ws3.Cell(row, 1).Value = city.GetProperty("name").GetString();
            ws3.Cell(row, 2).Value = city.GetProperty("users").GetInt64();
            row++;
        }
        ws3.Columns().AdjustToContents();

        using var stream = new System.IO.MemoryStream();
        workbook.SaveAs(stream);
        var content = stream.ToArray();

        string dateStr = startDate?.ToString("yyyy-MM-dd") ?? "7days";
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"StoreAnalytics_{dateStr}.xlsx");
    }

    private static decimal GrowthPct(decimal current, decimal previous) =>
        previous == 0 ? (current > 0 ? 100 : 0) :
        Math.Round((current - previous) / previous * 100, 1);
}

