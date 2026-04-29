using Sportive.API.Attributes;
using Sportive.API.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.ReportsMain)]
public class CashierPerformanceController : ControllerBase
{
    private readonly AppDbContext _db;
    public CashierPerformanceController(AppDbContext db) => _db = db;

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // GET /api/cashierperformance
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet]
    public async Task<IActionResult> GetPerformance(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] bool      excel    = false)
    {
        var from = fromDate?.Date ?? TimeHelper.GetEgyptTime().Date.AddDays(-30);
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        // â”€â”€ Ø¬Ù„Ø¨ Ø·Ù„Ø¨Ø§Øª Ø§Ù„ÙƒØ§Ø´ÙŠØ± ÙÙ‚Ø· (Source = POS) â”€â”€â”€â”€
        var orders = await _db.Orders
            .Include(o => o.Items)
            .Where(o => o.Status != OrderStatus.Cancelled
                     && o.Source == OrderSource.POS
                     && !string.IsNullOrEmpty(o.SalesPersonId)
                     && o.CreatedAt >= from
                     && o.CreatedAt <= to)
            .Select(o => new {
                o.Id, o.SalesPersonId, o.TotalAmount,
                o.DiscountAmount, ItemCount = o.Items.Sum(i => i.Quantity),
                o.PaymentMethod, o.Status, o.CreatedAt,
                o.OrderNumber,
                // Calculate return value for this specific order
                OrderReturnAmount = o.Status == OrderStatus.Returned ? o.TotalAmount : o.Items.Sum(i => i.Quantity > 0 ? (i.TotalPrice / i.Quantity) * i.ReturnedQuantity : 0)
            })
            .ToListAsync();

        if (!orders.Any())
            return Ok(new { from, to, cashiers = Array.Empty<object>(), summary = new { } });

        // â”€â”€ Ø¬Ù„Ø¨ Ø£Ø³Ù…Ø§Ø¡ Ø§Ù„ÙƒØ§Ø´ÙŠØ±ÙŠÙ† â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var cashierIds = orders.Select(o => o.SalesPersonId!).Distinct().ToList();
        var users = await _db.Users
            .Where(u => cashierIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.PhoneNumber })
            .ToDictionaryAsync(u => u.Id);

        // â”€â”€ ØªØ¬Ù…ÙŠØ¹ Ø­Ø³Ø¨ Ø§Ù„ÙƒØ§Ø´ÙŠØ± â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var cashierStats = orders
            .GroupBy(o => o.SalesPersonId!)
            .Select(g =>
            {
                var user       = users.GetValueOrDefault(g.Key);
                var name       = user?.FullName?.Trim() ?? "ÙƒØ§Ø´ÙŠØ± ØºÙŠØ± Ù…Ø¹Ø±ÙˆÙ";
                var allOrders  = g.ToList();
                var grossRevenue = allOrders.Sum(o => o.TotalAmount);
                var returnsAmount = allOrders.Sum(o => o.OrderReturnAmount);
                var netRevenue = grossRevenue - returnsAmount;
                var count      = allOrders.Count;
                var avgOrder   = count > 0 ? netRevenue / count : 0;
                var totalDisc  = allOrders.Sum(o => o.DiscountAmount);
                var totalItems = allOrders.Sum(o => o.ItemCount);
                var avgItems   = count > 0 ? (decimal)totalItems / count : 0;

                // Ù…Ø¨ÙŠØ¹Ø§Øª Ø¨Ø§Ù„Ø³Ø§Ø¹Ø© (Ù„ØªØ­Ø¯ÙŠØ¯ Ø³Ø§Ø¹Ø© Ø§Ù„Ø°Ø±ÙˆØ©)
                var byHour = allOrders
                    .GroupBy(o => o.CreatedAt.Hour)
                    .Select(h => new { hour = h.Key, count = h.Count(), revenue = h.Sum(o => o.TotalAmount) })
                    .OrderByDescending(h => h.revenue)
                    .ToList();

                var peakHour = byHour.FirstOrDefault();

                // Ù…Ø¨ÙŠØ¹Ø§Øª Ø¨Ø§Ù„Ø£Ø³Ø¨ÙˆØ¹
                var byDay = allOrders
                    .GroupBy(o => o.CreatedAt.DayOfWeek)
                    .Select(d => new { day = (int)d.Key, dayName = d.Key.ToString(), count = d.Count(), revenue = d.Sum(o => o.TotalAmount) })
                    .OrderBy(d => d.day)
                    .ToList();

                // Ø¢Ø®Ø± 30 ÙŠÙˆÙ… â€” Ù…Ø¨ÙŠØ¹Ø§Øª ÙŠÙˆÙ…ÙŠØ©
                var byDate = allOrders
                    .GroupBy(o => o.CreatedAt.Date)
                    .Select(d => new { date = d.Key.ToString("MM/dd"), revenue = d.Sum(o => o.TotalAmount), count = d.Count() })
                    .OrderBy(d => d.date)
                    .ToList();

                // Ø·Ø±Ù‚ Ø§Ù„Ø¯ÙØ¹
                var paymentBreakdown = allOrders
                    .GroupBy(o => o.PaymentMethod.ToString())
                    .Select(p => new { method = p.Key, count = p.Count(), revenue = p.Sum(o => o.TotalAmount) })
                    .OrderByDescending(p => p.count)
                    .ToList();

                // Ø£ÙƒØ¨Ø± ÙØ§ØªÙˆØ±Ø©
                var maxOrder = allOrders.OrderByDescending(o => o.TotalAmount).First();

                return new
                {
                    cashierId    = g.Key,
                    name,
                    phone        = user?.PhoneNumber,
                    // KPIs
                    revenue      = Math.Round(netRevenue, 2),
                    grossSales   = Math.Round(grossRevenue, 2),
                    returnsAmount= Math.Round(returnsAmount, 2),
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

        // â”€â”€ Ù…Ù„Ø®Øµ Ø¥Ø¬Ù…Ø§Ù„ÙŠ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var totalNetRevenue = cashierStats.Sum(c => c.revenue);
        var summary = new
        {
            totalRevenue   = totalNetRevenue,
            totalGrossSales = cashierStats.Sum(c => c.grossSales),
            totalReturns   = cashierStats.Sum(c => c.returnsAmount),
            totalDiscounts = cashierStats.Sum(c => c.totalDiscount),
            totalOrders    = cashierStats.Sum(c => c.orderCount),
            totalCashiers  = cashierStats.Count,
            avgOrderAll    = cashierStats.Any() ? Math.Round(cashierStats.Average(c => c.avgOrder), 2) : 0,
            topCashier     = cashierStats.FirstOrDefault()?.name,
            topRevenue     = cashierStats.FirstOrDefault()?.revenue ?? 0,
            // Ù…Ù‚Ø§Ø±Ù†Ø© Ø¨Ø§Ù„Ù†Ø³Ø¨Ø© Ø§Ù„Ù…Ø¦ÙˆÙŠØ© Ù„ÙƒÙ„ ÙƒØ§Ø´ÙŠØ±
            revenueShare   = cashierStats.Select(c => new {
                c.name,
                share = totalNetRevenue > 0 ? Math.Round(c.revenue / totalNetRevenue * 100, 1) : 0m,
            }),
        };

        if (excel) return ExportExcel(cashierStats, summary, from, to);

        return Ok(new { from, to, cashiers = cashierStats, summary });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Excel Export
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    private IActionResult ExportExcel(dynamic cashiers, dynamic summary, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Ø£Ø¯Ø§Ø¡ Ø§Ù„ÙƒØ§Ø´ÙŠØ±ÙŠÙ†");
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = $"ØªÙ‚Ø±ÙŠØ± Ø£Ø¯Ø§Ø¡ Ø§Ù„ÙƒØ§Ø´ÙŠØ±ÙŠÙ† â€” Ù…Ù† {from:yyyy-MM-dd} Ø¥Ù„Ù‰ {to:yyyy-MM-dd}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Range(1, 1, 1, 8).Merge();

        string[] headers = { "Ø§Ù„ÙƒØ§Ø´ÙŠØ±", "Ø¹Ø¯Ø¯ Ø§Ù„Ø·Ù„Ø¨Ø§Øª", "Ø¥Ø¬Ù…Ø§Ù„ÙŠ Ø§Ù„Ù…Ø¨ÙŠØ¹Ø§Øª", "Ø¥Ø¬Ù…Ø§Ù„ÙŠ Ø§Ù„Ù…Ø±ØªØ¬Ø¹Ø§Øª", "Ø§Ù„ØµØ§ÙÙŠ Ø§Ù„Ù…Ø­Ù‚Ù‚", "Ù…ØªÙˆØ³Ø· Ø§Ù„ÙØ§ØªÙˆØ±Ø©",
                              "Ø¥Ø¬Ù…Ø§Ù„ÙŠ Ø§Ù„Ø®ØµÙ…", "Ø¥Ø¬Ù…Ø§Ù„ÙŠ Ø§Ù„Ù‚Ø·Ø¹", "Ø£ÙƒØ¨Ø± ÙØ§ØªÙˆØ±Ø©", "Ø³Ø§Ø¹Ø© Ø§Ù„Ø°Ø±ÙˆØ©" };
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
            ws.Cell(r, 2).Value = (int)c.orderCount;
            ws.Cell(r, 3).Value = (decimal)c.grossSales;
            ws.Cell(r, 4).Value = (decimal)c.returnsAmount;
            ws.Cell(r, 5).Value = (decimal)c.revenue;
            ws.Cell(r, 6).Value = (decimal)c.avgOrder;
            ws.Cell(r, 7).Value = (decimal)c.totalDiscount;
            ws.Cell(r, 8).Value = (int)c.totalItems;
            ws.Cell(r, 9).Value = (decimal)c.maxOrderAmount;
            ws.Cell(r, 10).Value = $"{c.peakHour}:00";
            
            foreach (int col in new[] { 3, 4, 5, 6, 7, 9 })
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

