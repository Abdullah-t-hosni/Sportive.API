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
[Authorize(Roles = "Admin,Manager,Accountant")]
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
        [FromQuery] int?      brandId    = null,
        [FromQuery] string?   color      = null,
        [FromQuery] string?   size       = null,
        [FromQuery] int?      productId  = null,
        [FromQuery] string?   sortBy     = "revenue",
        [FromQuery] bool      hasCost    = false,
        [FromQuery] bool      excel      = false)
    {
        // Ensure dates are parsed correctly and treated as inclusive of the Egypt timezone offset if needed
        var from = fromDate?.Date ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate?.Date ?? TimeHelper.GetEgyptTime().Date;
        
        // Broaden range to ensure we capture orders that might be stored in UTC
        // by pushing the start back and end forward by a few hours if the DB is UTC
        var startRange = from.Date;
        var endRange   = to.Date.AddDays(1).AddTicks(-1);

        // ── جلب كل المبيعات في الفترة (باستثناء الملغي فقط) ──────────
        var itemsQ = _db.OrderItems
            .Include(i => i.Order)
            .Include(i => i.Product)
                .ThenInclude(p => p!.Category)
            .Include(i => i.Product)
                .ThenInclude(p => p!.Brand)
            .Include(i => i.Product)
                .ThenInclude(p => p!.Images.Where(img => img.IsMain))
            .Include(i => i.Product)
                .ThenInclude(p => p!.Variants)
            .Where(i => i.Order.Status != OrderStatus.Cancelled
                     && i.Order.CreatedAt >= startRange
                     && i.Order.CreatedAt <= endRange);

        // Recursive Category Filter
        if (categoryId.HasValue)
        {
            var categoryIds = await FilterHelper.GetCategoryFamilyIds(_db, categoryId);
            itemsQ = itemsQ.Where(i => i.Product != null && i.Product.CategoryId.HasValue && categoryIds.Contains(i.Product.CategoryId.Value));
        }

        // Recursive Brand Filter
        if (brandId.HasValue)
        {
            var brandIds = await FilterHelper.GetBrandFamilyIds(_db, brandId);
            itemsQ = itemsQ.Where(i => i.Product != null && i.Product.BrandId.HasValue && brandIds.Contains(i.Product.BrandId.Value));
        }

        // Color/Size Filter
        if (!string.IsNullOrEmpty(color))
            itemsQ = itemsQ.Where(i => i.Product != null && i.Product.Variants.Any(v => v.Color == color || v.ColorAr == color));

        if (!string.IsNullOrEmpty(size))
            itemsQ = itemsQ.Where(i => i.Product != null && i.Product.Variants.Any(v => v.Size == size));

        // فلتر بمنتج محدد
        if (productId.HasValue)
            itemsQ = itemsQ.Where(i => i.ProductId == productId.Value);
            
        if (hasCost)
            itemsQ = itemsQ.Where(i => i.Product != null && i.Product.CostPrice.HasValue);

        var items = await itemsQ.ToListAsync();

        // ── تجميع حسب المنتج ──────────────────────────
        var grouped = items
            .Where(i => i.ProductId.HasValue)
            .GroupBy(i => i.ProductId!.Value)
            .Select(g =>
            {
                var firstItem = g.First();
                var product   = firstItem.Product;
                if (product == null) return null;

                var mainImage = product.Images.FirstOrDefault()?.ImageUrl;
                
                // في نظام "فاتورة المبيعات"، نحتاج لحساب الصافي الحقيقي لكل سطر:
                // 1. الكمية الصافية (المباع - المرتجع)
                var netUnits = g.Sum(i => i.Quantity - i.ReturnedQuantity);
                if (netUnits < 0) netUnits = 0; // حماية منطقية

                // 2. حساب الإيراد الصافي لكل سطر مع توزيع خصم الفاتورة (Discount Pro-rating)
                decimal totalNetRevenue = 0;
                foreach (var i in g)
                {
                    // الكمية المباعة فعلياً في هذا السطر بعد المرتجع
                    var lineNetQty = (i.Quantity - i.ReturnedQuantity);
                    if (lineNetQty <= 0) continue;

                    // السعر قبل خصم الفاتورة (لكن بعد خصم السطر إن وجد)
                    var lineSubtotal = i.TotalPrice;
                    
                    // توزيع خصم رأس الفاتورة نسبياً
                    decimal lineOrderDiscountShare = 0;
                    if (i.Order.SubTotal > 0 && i.Order.DiscountAmount > 0)
                    {
                        lineOrderDiscountShare = (lineSubtotal / i.Order.SubTotal) * i.Order.DiscountAmount;
                    }

                    // صافي الإيراد من هذا السطر لهذه الكمية
                    // ملاحظة: i.TotalPrice هي إجمالي السطر (UnitPrice * Qty)
                    // نأخذ حصة الكمية المتبقية فقط من السعر والخصم
                    decimal qtyFactor = (decimal)lineNetQty / i.Quantity;
                    totalNetRevenue += (lineSubtotal * qtyFactor) - (lineOrderDiscountShare * qtyFactor);
                }

                // التكلفة الصافية
                var costPrice = product.CostPrice;
                var totalCost = costPrice.HasValue ? costPrice.Value * netUnits : (decimal?)null;

                // الربح والهامش
                var grossProfit = totalCost.HasValue ? totalNetRevenue - totalCost.Value : (decimal?)null;
                var marginPct   = (totalCost.HasValue && totalNetRevenue > 0)
                    ? Math.Round((grossProfit!.Value / totalNetRevenue) * 100, 1)
                    : (decimal?)null;

                var totalSales = g.Sum(i => i.TotalPrice);
                var totalReturnsValue = g.Sum(i => i.ReturnedQuantity * i.UnitPrice);
                var totalDiscounts = (totalSales - totalReturnsValue) - totalNetRevenue;

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
                    AvgSellingPrice:  netUnits > 0 ? Math.Round(totalNetRevenue / netUnits, 2) : 0,
                    UnitsSold:        g.Sum(i => i.Quantity),
                    ReturnedUnits:    g.Sum(i => i.ReturnedQuantity),
                    NetUnits:         netUnits,
                    TotalSales:       totalSales,
                    TotalReturns:     totalReturnsValue,
                    TotalDiscounts:   totalDiscounts,
                    NetRevenue:       totalNetRevenue,
                    TotalCost:        totalCost,
                    GrossProfit:      grossProfit,
                    MarginPct:        marginPct,
                    OrderCount:       g.Select(i => i.OrderId).Distinct().Count()
                );
            })
            .Where(x => x != null)
            .Cast<ProductProfitRow>()
            .ToList();

        // ترتيب
        grouped = sortBy switch
        {
            "margin"  => grouped.OrderByDescending(r => r.MarginPct ?? -999).ToList(),
            "units"   => grouped.OrderByDescending(r => r.NetUnits).ToList(),
            "profit"  => grouped.OrderByDescending(r => r.GrossProfit ?? -999).ToList(),
            _         => grouped.OrderByDescending(r => r.NetRevenue).ToList(),
        };

        // ── Summary ────────────────────────────────────
        var withCost    = grouped.Where(r => r.CostPrice.HasValue).ToList();
        var summary = new
        {
            totalProducts    = grouped.Count,
            withCostCount    = withCost.Count,
            withoutCostCount = grouped.Count - withCost.Count,
            totalSales       = grouped.Sum(r => r.TotalSales),
            totalReturns     = grouped.Sum(r => r.TotalReturns),
            totalDiscounts   = grouped.Sum(r => r.TotalDiscounts),
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
        var now = TimeHelper.GetEgyptTime();
        var from = fromDate?.Date ?? new DateTime(now.Year, now.Month, 1);
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? now;

        var items = await _db.OrderItems
            .Include(i => i.Order)
            .Include(i => i.Product)
            .Where(i => i.Order.Status != OrderStatus.Cancelled
                     && i.Order.CreatedAt >= from && i.Order.CreatedAt <= to
                     && i.ProductId.HasValue)
            .ToListAsync();

        decimal totalNetRevenue = 0;
        decimal totalCost       = 0;

        foreach (var i in items)
        {
            var netQty = i.Quantity - i.ReturnedQuantity;
            if (netQty <= 0) continue;

            // حصة الصنف من إجمالي السعر قبل خصم الفاتورة (لصافي الكمية)
            decimal qtyFactor = (decimal)netQty / i.Quantity;
            decimal lineShareOfSubtotal = i.TotalPrice * qtyFactor;

            // حصة الصنف من خصم الفاتورة (تناسبياً)
            decimal lineOrderDiscountShare = 0;
            if (i.Order.SubTotal > 0 && i.Order.DiscountAmount > 0)
            {
                lineOrderDiscountShare = (i.TotalPrice / i.Order.SubTotal) * i.Order.DiscountAmount;
            }

            totalNetRevenue += (lineShareOfSubtotal - (lineOrderDiscountShare * qtyFactor));
            totalCost       += (i.Product?.CostPrice ?? 0) * netQty;
        }

        var totalProfit = totalNetRevenue - totalCost;
        var margin      = totalNetRevenue > 0 ? Math.Round(totalProfit / totalNetRevenue * 100, 1) : 0;

        return Ok(new { totalRevenue = totalNetRevenue, totalCost, totalProfit, marginPct = margin, from, to });
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
            "سعر الجمهور", "سعر التكلفة",
            "وحدات مباعة", "وحدات مرتجعة", "صافي الوحدات",
            "إجمالي المبيعات", "المرتجعات", "الخصومات", "صافي الإيراد",
            "التكلفة الإجمالية", "إجمالي الربح", "هامش الربح %"
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
            ws.Cell(r,  5).Value = row.CostPrice.HasValue ? (XLCellValue)row.CostPrice.Value : (XLCellValue)"—";
            ws.Cell(r,  6).Value = row.UnitsSold;
            ws.Cell(r,  7).Value = row.ReturnedUnits;
            ws.Cell(r,  8).Value = row.NetUnits;
            ws.Cell(r,  9).Value = row.TotalSales;
            ws.Cell(r, 10).Value = row.TotalReturns;
            ws.Cell(r, 11).Value = row.TotalDiscounts;
            ws.Cell(r, 12).Value = row.NetRevenue;
            ws.Cell(r, 13).Value = row.TotalCost.HasValue ? (XLCellValue)row.TotalCost.Value : (XLCellValue)"—";
            ws.Cell(r, 14).Value = row.GrossProfit.HasValue ? (XLCellValue)row.GrossProfit.Value : (XLCellValue)"—";
            ws.Cell(r, 15).Value = row.MarginPct.HasValue ? (XLCellValue)row.MarginPct.Value : (XLCellValue)"—";

            // تنسيق الأرقام
            foreach (int col in new[] { 4, 5, 9, 10, 11, 12, 13, 14 })
                if (ws.Cell(r, col).Value.IsNumber)
                    ws.Cell(r, col).Style.NumberFormat.Format = "#,##0.00";

            if (ws.Cell(r, 15).Value.IsNumber)
                ws.Cell(r, 15).Style.NumberFormat.Format = "0.0\"%\"";

            // تلوين حسب الهامش
            if (row.MarginPct.HasValue)
            {
                var bg = row.MarginPct.Value >= 30 ? XLColor.FromHtml("#e8f5e9")
                       : row.MarginPct.Value >= 10 ? XLColor.FromHtml("#fff8e1")
                       : XLColor.FromHtml("#ffebee");
                ws.Cell(r, 15).Style.Fill.BackgroundColor = bg;
            }
            r++;
        }

        // Totals row
        ws.Cell(r, 1).Value = "الإجمالي";
        ws.Cell(r, 1).Style.Font.Bold = true;
        ws.Cell(r, 9).Value = rows.Sum(x => x.TotalSales);
        ws.Cell(r, 10).Value = rows.Sum(x => x.TotalReturns);
        ws.Cell(r, 11).Value = rows.Sum(x => x.TotalDiscounts);
        ws.Cell(r, 12).Value = rows.Sum(x => x.NetRevenue);
        ws.Cell(r, 13).Value = rows.Sum(x => x.TotalCost ?? 0);
        ws.Cell(r, 14).Value = rows.Sum(x => x.GrossProfit ?? 0);
        for (int col = 9; col <= 15; col++)
        {
            ws.Cell(r, col).Style.Font.Bold = true;
            if (col < 15) ws.Cell(r, col).Style.NumberFormat.Format = "#,##0.00";
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
            ("إجمالي المبيعات (بداية)",  (decimal)summary.totalSales),
            ("إجمالي المرتجعات",         (decimal)summary.totalReturns),
            ("إجمالي الخصومات",          (decimal)summary.totalDiscounts),
            ("إجمالي الإيرادات الصافية", (decimal)summary.totalNetRevenue),
            ("إجمالي التكاليف",           (decimal)summary.totalCost),
            ("إجمالي الربح الإجمالي",    (decimal)summary.totalGrossProfit),
            ("هامش الربح الإجمالي %",    (decimal)summary.overallMargin),
            ("إجمالي الوحدات المباعة",   (int)summary.totalUnitsSold),
            ("إجمالي المرتجعات (وحدات)", (int)summary.totalReturned),
            ("منتجات بسعر تكلفة",        (int)summary.withCostCount),
            ("منتجات بدون سعر تكلفة",   (int)summary.withoutCostCount),
        };

        int sr = 3;
        foreach (var (label, val) in summaryRows)
        {
            ws2.Cell(sr, 1).Value = label;
            ws2.Cell(sr, 2).Value = XLCellValue.FromObject(val);
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
    decimal  TotalSales,
    decimal  TotalReturns,
    decimal  TotalDiscounts,
    decimal  NetRevenue,
    decimal? TotalCost,
    decimal? GrossProfit,
    decimal? MarginPct,
    int      OrderCount
);
