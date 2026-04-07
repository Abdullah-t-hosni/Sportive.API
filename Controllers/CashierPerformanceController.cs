using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager")]
public class CashierPerformanceController : ControllerBase
{
    private readonly AppDbContext _db;
    public CashierPerformanceController(AppDbContext db) => _db = db;

    // ══════════════════════════════════════════════════
    // GET /api/cashierperformance
    // ══════════════════════════════════════════════════
    [HttpGet]
    public async Task<IActionResult> GetPerformance(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] bool      excel    = false)
    {
        var from = fromDate ?? DateTime.UtcNow.Date.AddDays(-30);
        var to   = toDate   ?? DateTime.UtcNow;

        // ── جلب طلبات الكاشير فقط (Source = POS) ────
        var orders = await _db.Orders
            .Include(o => o.Items)
            .Where(o => !o.IsDeleted
                     && o.Status != OrderStatus.Cancelled
                     && o.Source == OrderSource.POS
                     && !string.IsNullOrEmpty(o.SalesPersonId)
                     && o.CreatedAt >= from
                     && o.CreatedAt <= to)
            .Select(o => new {
                o.Id, o.SalesPersonId, o.TotalAmount,
                o.DiscountAmount, ItemCount = o.Items.Sum(i => i.Quantity),
                o.PaymentMethod, o.Status, o.CreatedAt,
                o.OrderNumber,
            })
            .ToListAsync();

        if (!orders.Any())
            return Ok(new { from, to, cashiers = Array.Empty<object>(), summary = new { } });

        // ── جلب أسماء الكاشيرين ──────────────────────
        var cashierIds = orders.Select(o => o.SalesPersonId!).Distinct().ToList();
        var users = await _db.Users
            .Where(u => cashierIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.PhoneNumber })
            .ToDictionaryAsync(u => u.Id);

        // ── تجميع حسب الكاشير ─────────────────────────
        var cashierStats = orders
            .GroupBy(o => o.SalesPersonId!)
            .Select(g =>
            {
                var user       = users.GetValueOrDefault(g.Key);
                var name       = user?.FullName?.Trim() ?? "كاشير غير معروف";
                var allOrders  = g.ToList();
                var revenue    = allOrders.Sum(o => o.TotalAmount);
                var count      = allOrders.Count;
                var avgOrder   = count > 0 ? revenue / count : 0;
                var totalDisc  = allOrders.Sum(o => o.DiscountAmount);
                var totalItems = allOrders.Sum(o => o.ItemCount);
                var avgItems   = count > 0 ? (decimal)totalItems / count : 0;

                // مبيعات بالساعة (لتحديد ساعة الذروة)
                var byHour = allOrders
                    .GroupBy(o => o.CreatedAt.Hour)
                    .Select(h => new { hour = h.Key, count = h.Count(), revenue = h.Sum(o => o.TotalAmount) })
                    .OrderByDescending(h => h.revenue)
                    .ToList();

                var peakHour = byHour.FirstOrDefault();

                // مبيعات بالأسبوع
                var byDay = allOrders
                    .GroupBy(o => o.CreatedAt.DayOfWeek)
                    .Select(d => new { day = (int)d.Key, dayName = d.Key.ToString(), count = d.Count(), revenue = d.Sum(o => o.TotalAmount) })
                    .OrderBy(d => d.day)
                    .ToList();

                // آخر 30 يوم — مبيعات يومية
                var byDate = allOrders
                    .GroupBy(o => o.CreatedAt.Date)
                    .Select(d => new { date = d.Key.ToString("MM/dd"), revenue = d.Sum(o => o.TotalAmount), count = d.Count() })
                    .OrderBy(d => d.date)
                    .ToList();

                // طرق الدفع
                var paymentBreakdown = allOrders
                    .GroupBy(o => o.PaymentMethod.ToString())
                    .Select(p => new { method = p.Key, count = p.Count(), revenue = p.Sum(o => o.TotalAmount) })
                    .OrderByDescending(p => p.count)
                    .ToList();

                // أكبر فاتورة
                var maxOrder = allOrders.OrderByDescending(o => o.TotalAmount).First();

                return new
                {
                    cashierId    = g.Key,
                    name,
                    phone        = user?.PhoneNumber,
                    // KPIs
                    revenue      = Math.Round(revenue, 2),
                    orderCount   = count,
                    avgOrder     = Math.Round(avgOrder, 2),
                    totalDiscount= Math.Round(totalDisc, 2),
                    totalItems,
                    avgItemsPerOrder = Math.Round(avgItems, 1),
                    maxOrderAmount   = maxOrder.TotalAmount,
                    maxOrderNumber   = maxOrder.OrderNumber,
                    // Breakdowns
                    peakHour     = peakHour?.hour,
                    peakHourRevenue = peakHour?.revenue ?? 0,
                    byHour,
                    byDay,
                    byDate,
                    paymentBreakdown,
                };
            })
            .OrderByDescending(c => c.revenue)
            .ToList();

        // ── ملخص إجمالي ───────────────────────────────
        var totalRevenue = cashierStats.Sum(c => c.revenue);
        var summary = new
        {
            totalRevenue,
            totalOrders    = cashierStats.Sum(c => c.orderCount),
            totalCashiers  = cashierStats.Count,
            avgOrderAll    = cashierStats.Any() ? Math.Round(cashierStats.Average(c => c.avgOrder), 2) : 0,
            topCashier     = cashierStats.FirstOrDefault()?.name,
            topRevenue     = cashierStats.FirstOrDefault()?.revenue ?? 0,
            // مقارنة بالنسبة المئوية لكل كاشير
            revenueShare   = cashierStats.Select(c => new {
                c.name,
                share = totalRevenue > 0 ? Math.Round(c.revenue / totalRevenue * 100, 1) : 0m,
            }),
        };

        if (excel) return ExportExcel(cashierStats, summary, from, to);

        return Ok(new { from, to, cashiers = cashierStats, summary });
    }

    // ══════════════════════════════════════════════════
    // Excel Export
    // ══════════════════════════════════════════════════
    private IActionResult ExportExcel(dynamic cashiers, dynamic summary, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("أداء الكاشيرين");
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = $"تقرير أداء الكاشيرين — من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Range(1, 1, 1, 8).Merge();

        string[] headers = { "الكاشير", "عدد الطلبات", "إجمالي المبيعات", "متوسط الفاتورة",
                              "إجمالي الخصم", "إجمالي القطع", "أكبر فاتورة", "ساعة الذروة" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(2, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f3460");
            cell.Style.Font.FontColor = XLColor.White;
        }

        int r = 3;
        foreach (var c in cashiers)
        {
            ws.Cell(r, 1).Value = c.name;
            ws.Cell(r, 2).Value = c.orderCount;
            ws.Cell(r, 3).Value = c.revenue;
            ws.Cell(r, 4).Value = c.avgOrder;
            ws.Cell(r, 5).Value = c.totalDiscount;
            ws.Cell(r, 6).Value = c.totalItems;
            ws.Cell(r, 7).Value = c.maxOrderAmount;
            ws.Cell(r, 8).Value = $"{c.peakHour}:00";
            foreach (int col in new[] { 3, 4, 5, 7 })
                ws.Cell(r, col).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }
        ws.Columns().AdjustToContents();

        var s = new MemoryStream();
        wb.SaveAs(s); s.Position = 0;
        return new FileStreamResult(s,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            { FileDownloadName = $"cashier_performance_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx" };
    }
}
