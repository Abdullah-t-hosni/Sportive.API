using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class ProfitabilityController : ControllerBase
{
    private readonly AppDbContext _db;
    public ProfitabilityController(AppDbContext db) => _db = db;

    // ══════════════════════════════════════════════════
    // GET /api/profitability/products
    // ══════════════════════════════════════════════════
    [HttpGet("products")]
    public async Task<IActionResult> GetProductProfitability(
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] int?      categoryId = null,
        [FromQuery] string?   sortBy     = "revenue",  // revenue | margin | units | profit
        [FromQuery] bool      hasCost    = false,       // فقط المنتجات التي لديها CostPrice
        [FromQuery] bool      excel      = false)
    {
        var from = fromDate ?? new DateTime(DateTime.UtcNow.Year, 1, 1);
        var to   = toDate   ?? DateTime.UtcNow;

        // ── جلب كل المبيعات في الفترة ─────────────────
        var itemsQ = _db.OrderItems
            .Include(i => i.Order)
            .Include(i => i.Product)
                .ThenInclude(p => p.Category)
            .Include(i => i.Product)
                .ThenInclude(p => p.Images.Where(img => img.IsMain && !img.IsDeleted))
            .Where(i => !i.IsDeleted
                     && !i.Order.IsDeleted
                     && i.Order.Status != OrderStatus.Cancelled
                     && i.Order.Status != OrderStatus.Returned
                     && i.Order.CreatedAt >= from
                     && i.Order.CreatedAt <= to);

        if (categoryId.HasValue)
            itemsQ = itemsQ.Where(i => i.Product.CategoryId == categoryId.Value);

        var items = await itemsQ.ToListAsync();

        // ── المرتجعات (نخصمها من الإيرادات) ──────────
        var returnedItems = await _db.OrderItems
            .Include(i => i.Order)
            .Where(i => !i.IsDeleted
                     && !i.Order.IsDeleted
                     && i.Order.Status == OrderStatus.Returned
                     && i.Order.CreatedAt >= from
                     && i.Order.CreatedAt <= to)
            .ToListAsync();

        // ── تجميع حسب المنتج ──────────────────────────
        var grouped = items
            .GroupBy(i => i.ProductId)
            .Select(g =>
            {
                var product      = g.First().Product;
                var mainImage    = product.Images.FirstOrDefault()?.ImageUrl;
                var unitsSold    = g.Sum(i => i.Quantity);
                var grossRevenue = g.Sum(i => i.TotalPrice);

                // المرتجعات لهذا المنتج
                var returnedUnits   = returnedItems.Where(r => r.ProductId == g.Key).Sum(r => r.Quantity);
                var returnedRevenue = returnedItems.Where(r => r.ProductId == g.Key).Sum(r => r.TotalPrice);

                // صافي المبيعات
                var netUnits   = unitsSold - returnedUnits;
                var netRevenue = grossRevenue - returnedRevenue;

                // التكلفة
                var costPrice  = product.CostPrice;
                var totalCost  = costPrice.HasValue ? costPrice.Value * netUnits : (decimal?)null;

                // هامش الربح
                var grossProfit    = totalCost.HasValue ? netRevenue - totalCost.Value : (decimal?)null;
                var marginPct      = (totalCost.HasValue && netRevenue > 0)
                    ? Math.Round((grossProfit!.Value / netRevenue) * 100, 1)
                    : (decimal?)null;

                // متوسط سعر البيع الفعلي
                var avgSellingPrice = netUnits > 0
                    ? Math.Round(netRevenue / netUnits, 2) : 0;

                return new ProductProfitRow(
                    ProductId:        g.Key,
                    ProductNameAr:    product.NameAr,
                    ProductNameEn:    product.NameEn ?? "",
                    SKU:              product.SKU,
                    CategoryName:     product.Category?.NameAr ?? "",
                    Image:            mainImage,
                    ListPrice:        product.Price,
                    DiscountPrice:    product.DiscountPrice,
                    CostPrice:        costPrice,
                    AvgSellingPrice:  avgSellingPrice,
                    UnitsSold:        unitsSold,
                    ReturnedUnits:    returnedUnits,
                    NetUnits:         netUnits,
                    GrossRevenue:     grossRevenue,
                    ReturnedRevenue:  returnedRevenue,
                    NetRevenue:       netRevenue,
                    TotalCost:        totalCost,
                    GrossProfit:      grossProfit,
                    MarginPct:        marginPct,
                    OrderCount:       g.Select(i => i.OrderId).Distinct().Count()
                );
            })
            .ToList();

        // فلتر المنتجات اللي عندها cost
        if (hasCost)
            grouped = grouped.Where(r => r.CostPrice.HasValue).ToList();

        // ترتيب
        grouped = sortBy switch
        {
            "margin"  => grouped.OrderByDescending(r => r.MarginPct ?? -999).ToList(),
            "units"   => grouped.OrderByDescending(r => r.NetUnits).ToList(),
            "profit"  => grouped.OrderByDescending(r => r.GrossProfit ?? -999).ToList(),
            "returns" => grouped.OrderByDescending(r => r.ReturnedUnits).ToList(),
            _         => grouped.OrderByDescending(r => r.NetRevenue).ToList(),
        };

        // ── Summary ────────────────────────────────────
        var withCost    = grouped.Where(r => r.CostPrice.HasValue).ToList();
        var summary = new
        {
            totalProducts    = grouped.Count,
            withCostCount    = withCost.Count,
            withoutCostCount = grouped.Count - withCost.Count,
            totalNetRevenue  = grouped.Sum(r => r.NetRevenue),
            totalCost        = withCost.Sum(r => r.TotalCost ?? 0),
            totalGrossProfit = withCost.Sum(r => r.GrossProfit ?? 0),
            overallMargin    = withCost.Sum(r => r.NetRevenue) > 0
                ? Math.Round(withCost.Sum(r => r.GrossProfit ?? 0) / withCost.Sum(r => r.NetRevenue) * 100, 1)
                : 0m,
            totalUnitsSold   = grouped.Sum(r => r.UnitsSold),
            totalReturned    = grouped.Sum(r => r.ReturnedUnits),
            // أعلى هامش ربح
            topMarginProduct = withCost.OrderByDescending(r => r.MarginPct).FirstOrDefault()?.ProductNameAr,
            topMarginPct     = withCost.OrderByDescending(r => r.MarginPct).FirstOrDefault()?.MarginPct,
            // أقل هامش ربح (أو خسارة)
            lowestMarginProduct = withCost.OrderBy(r => r.MarginPct).FirstOrDefault()?.ProductNameAr,
            lowestMarginPct     = withCost.OrderBy(r => r.MarginPct).FirstOrDefault()?.MarginPct,
        };

        if (excel) return ExcelProfitability(grouped, summary, from, to);

        return Ok(new { from, to, summary, products = grouped });
    }

    // ══════════════════════════════════════════════════
    // GET /api/profitability/summary
    // ملخص سريع للداشبورد
    // ══════════════════════════════════════════════════
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null)
    {
        var now2 = DateTime.UtcNow;
        var from = fromDate ?? new DateTime(now2.Year, now2.Month, 1);
        var to   = toDate   ?? DateTime.UtcNow;

        var items = await _db.OrderItems
            .Include(i => i.Order)
            .Include(i => i.Product)
            .Where(i => !i.IsDeleted && !i.Order.IsDeleted
                     && i.Order.Status != OrderStatus.Cancelled
                     && i.Order.Status != OrderStatus.Returned
                     && i.Order.CreatedAt >= from && i.Order.CreatedAt <= to
                     && i.Product.CostPrice != null)
            .ToListAsync();

        var totalRevenue = items.Sum(i => i.TotalPrice);
        var totalCost    = items.Sum(i => (i.Product.CostPrice ?? 0) * i.Quantity);
        var totalProfit  = totalRevenue - totalCost;
        var margin       = totalRevenue > 0 ? Math.Round(totalProfit / totalRevenue * 100, 1) : 0;

        return Ok(new { totalRevenue, totalCost, totalProfit, marginPct = margin, from, to });
    }

    // ══════════════════════════════════════════════════
    // EXCEL EXPORT
    // ══════════════════════════════════════════════════
    private IActionResult ExcelProfitability(
        List<ProductProfitRow> rows, dynamic summary, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();

        // ── Sheet 1: تفاصيل المنتجات ──────────────────
        var ws = wb.Worksheets.Add("ربحية المنتجات");
        ws.RightToLeft = true;

        // Title
        ws.Cell(1, 1).Value = $"تقرير ربحية المنتجات — من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";
        ws.Cell(1, 1).Style.Font.Bold     = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Range(1, 1, 1, 12).Merge();

        // Headers
        string[] h = {
            "اسم المنتج", "SKU", "الفئة",
            "سعر الجمهور", "سعر البيع الفعلي", "سعر التكلفة",
            "وحدات مباعة", "وحدات مرتجعة", "صافي الوحدات",
            "صافي الإيرادات", "التكلفة الإجمالية",
            "إجمالي الربح", "هامش الربح %"
        };
        for (int c = 0; c < h.Length; c++)
        {
            var cell = ws.Cell(2, c + 1);
            cell.Value = h[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f3460");
            cell.Style.Font.FontColor       = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int r = 3;
        foreach (var row in rows)
        {
            ws.Cell(r,  1).Value = row.ProductNameAr;
            ws.Cell(r,  2).Value = row.SKU;
            ws.Cell(r,  3).Value = row.CategoryName;
            ws.Cell(r,  4).Value = row.ListPrice;
            ws.Cell(r,  5).Value = row.AvgSellingPrice;
            ws.Cell(r,  6).Value = row.CostPrice.HasValue ? row.CostPrice.Value : (object)"—";
            ws.Cell(r,  7).Value = row.UnitsSold;
            ws.Cell(r,  8).Value = row.ReturnedUnits;
            ws.Cell(r,  9).Value = row.NetUnits;
            ws.Cell(r, 10).Value = row.NetRevenue;
            ws.Cell(r, 11).Value = row.TotalCost.HasValue ? row.TotalCost.Value : (object)"لا تكلفة";
            ws.Cell(r, 12).Value = row.GrossProfit.HasValue ? row.GrossProfit.Value : (object)"—";
            ws.Cell(r, 13).Value = row.MarginPct.HasValue  ? row.MarginPct.Value   : (object)"—";

            // تنسيق الأرقام
            foreach (int col in new[] { 4, 5, 6, 10, 11, 12 })
                if (ws.Cell(r, col).Value.IsNumber)
                    ws.Cell(r, col).Style.NumberFormat.Format = "#,##0.00";

            if (ws.Cell(r, 13).Value.IsNumber)
                ws.Cell(r, 13).Style.NumberFormat.Format = "0.0\"%\"";

            // تلوين حسب الهامش
            if (row.MarginPct.HasValue)
            {
                var bg = row.MarginPct.Value >= 30 ? XLColor.FromHtml("#e8f5e9")
                       : row.MarginPct.Value >= 10 ? XLColor.FromHtml("#fff8e1")
                       : XLColor.FromHtml("#ffebee");
                ws.Cell(r, 13).Style.Fill.BackgroundColor = bg;
            }
            r++;
        }

        // Totals row
        ws.Cell(r, 1).Value = "الإجمالي";
        ws.Cell(r, 1).Style.Font.Bold = true;
        ws.Cell(r, 10).Value = rows.Sum(x => x.NetRevenue);
        ws.Cell(r, 11).Value = rows.Sum(x => x.TotalCost ?? 0);
        ws.Cell(r, 12).Value = rows.Sum(x => x.GrossProfit ?? 0);
        for (int col = 10; col <= 13; col++)
        {
            ws.Cell(r, col).Style.Font.Bold = true;
            if (col < 13) ws.Cell(r, col).Style.NumberFormat.Format = "#,##0.00";
        }
        ws.Row(r).Style.Fill.BackgroundColor = XLColor.FromHtml("#e8f5e9");

        ws.Columns().AdjustToContents();

        // ── Sheet 2: ملخص تنفيذي ──────────────────────
        var ws2 = wb.Worksheets.Add("الملخص التنفيذي");
        ws2.RightToLeft = true;

        ws2.Cell(1, 1).Value = "الملخص التنفيذي";
        ws2.Cell(1, 1).Style.Font.Bold = true;
        ws2.Cell(1, 1).Style.Font.FontSize = 14;

        var summaryRows = new (string Label, object Value)[]
        {
            ("إجمالي الإيرادات الصافية", (decimal)summary.totalNetRevenue),
            ("إجمالي التكاليف",           (decimal)summary.totalCost),
            ("إجمالي الربح الإجمالي",    (decimal)summary.totalGrossProfit),
            ("هامش الربح الإجمالي %",    (decimal)summary.overallMargin),
            ("إجمالي الوحدات المباعة",   (int)summary.totalUnitsSold),
            ("إجمالي المرتجعات",         (int)summary.totalReturned),
            ("منتجات بسعر تكلفة",        (int)summary.withCostCount),
            ("منتجات بدون سعر تكلفة",   (int)summary.withoutCostCount),
        };

        int sr = 3;
        foreach (var (label, val) in summaryRows)
        {
            ws2.Cell(sr, 1).Value = label;
            ws2.Cell(sr, 2).Value = val;
            if (val is decimal d && (label.Contains("إيراد") || label.Contains("تكلف") || label.Contains("ربح")))
                ws2.Cell(sr, 2).Style.NumberFormat.Format = "#,##0.00";
            if (label.Contains("%"))
                ws2.Cell(sr, 2).Style.NumberFormat.Format = "0.0\"%\"";
            sr++;
        }
        ws2.Columns().AdjustToContents();

        return ExcelFile(wb, $"profitability_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }

    private static FileStreamResult ExcelFile(XLWorkbook wb, string name)
    {
        var s = new MemoryStream();
        wb.SaveAs(s); s.Position = 0;
        return new FileStreamResult(s,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            { FileDownloadName = name };
    }
}

// ── Report DTO ────────────────────────────────────────
public record ProductProfitRow(
    int      ProductId,
    string   ProductNameAr,
    string   ProductNameEn,
    string   SKU,
    string   CategoryName,
    string?  Image,
    decimal  ListPrice,
    decimal? DiscountPrice,
    decimal? CostPrice,
    decimal  AvgSellingPrice,
    int      UnitsSold,
    int      ReturnedUnits,
    int      NetUnits,
    decimal  GrossRevenue,
    decimal  ReturnedRevenue,
    decimal  NetRevenue,
    decimal? TotalCost,
    decimal? GrossProfit,
    decimal? MarginPct,
    int      OrderCount
);
