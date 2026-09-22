using Sportive.API.Attributes;
using Sportive.API.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Extensions;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.ReportsMain)]
public class ProfitabilityController : ControllerBase
{
    private readonly AppDbContext _db;
    public ProfitabilityController(AppDbContext db) => _db = db;

    // ======================================================================
    // GET /api/profitability/products
    // ======================================================================
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
        [FromQuery] bool      excel      = false,
        [FromQuery] int?      branchId   = null)
    {
        // Ensure dates are parsed correctly and treated as inclusive of the Egypt timezone offset if needed
        var from = fromDate?.Date ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate?.Date ?? TimeHelper.GetEgyptTime().Date;
        
        // Broaden range to ensure we capture orders that might be stored in UTC
        // by pushing the start back and end forward by a few hours if the DB is UTC
        var startRange = from.Date;
        var endRange   = to.Date.AddDays(1).AddTicks(-1);

        int? isolatedBranchId = await User.HasViewAllBranchesAsync(HttpContext) ? branchId : User.GetBranchId();

        // --- جلب كل المبيعات في الفترة (باستثناء الملغي فقط) ------------------
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

        if (isolatedBranchId.HasValue)
        {
            itemsQ = itemsQ.Where(i => i.Order.BranchId == isolatedBranchId.Value);
        }

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

        // --- تجميع حسب المنتج ----------------------------
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
                decimal totalNetRevenue      = 0;
                decimal totalItemDiscount    = 0;
                foreach (var i in g)
                {
                    // الكمية المباعة فعلياً في هذا السطر بعد المرتجع
                    var lineNetQty = (i.Quantity - i.ReturnedQuantity);
                    if (lineNetQty <= 0) continue;

                    // السعر قبل خصم الفاتورة (لكن بعد خصم السطر إن وجد)
                    var lineSubtotal = i.TotalPrice;
                    
                    // توزيع خصم رأس الفاتورة نسبياً فقط (بدون إدخال رسوم الشحن في ربحية المنتج)
                    decimal lineOrderDiscountShare = 0;

                    if (i.Order.SubTotal > 0)
                    {
                        var ratio = lineSubtotal / i.Order.SubTotal;
                        lineOrderDiscountShare = ratio * i.Order.DiscountAmount;
                    }

                    // صافي الإيراد من هذا السطر لهذه الكمية
                    decimal qtyFactor = (decimal)lineNetQty / i.Quantity;
                    // الإيراد = سعر السطر - نصيبه من خصم الفاتورة (ولا يشمل الشحن)
                    totalNetRevenue += (lineSubtotal * qtyFactor) - (lineOrderDiscountShare * qtyFactor);
                    
                    // إجمالي الخصومات الموزعة (خصم الصنف + خصم الفاتورة النسبي)
                    totalItemDiscount += i.DiscountAmount + (lineOrderDiscountShare * qtyFactor);
                }

                // التكلفة الصافية (تكلفة المنتج فقط)
                // FIFO COST: Fetch the actual cost recorded in the inventory movement for this order
                var orderNumbers = g.Select(i => i.Order.OrderNumber).Distinct().ToList();
                var movements = _db.InventoryMovements
                    .Where(m => m.Type == InventoryMovementType.Sale && m.Reference != null && orderNumbers.Contains(m.Reference) && m.ProductId == product.Id)
                    .ToList();

                decimal totalCost = 0;
                bool costFound = false;

                foreach (var i in g)
                {
                    var m = movements.FirstOrDefault(x => x.Reference == i.Order.OrderNumber && x.ProductVariantId == i.ProductVariantId);
                    if (m != null)
                    {
                        totalCost += m.UnitCost * (i.Quantity - i.ReturnedQuantity);
                        costFound = true;
                    }
                    else if (product.CostPrice.HasValue)
                    {
                        totalCost += product.CostPrice.Value * (i.Quantity - i.ReturnedQuantity);
                        costFound = true;
                    }
                }

                // Profit
                var grossProfit = costFound ? totalNetRevenue - totalCost : (decimal?)null;
                var marginPct   = (totalNetRevenue > 0 && costFound)
                    ? Math.Round((grossProfit!.Value / totalNetRevenue) * 100, 1)
                    : (decimal?)null;

                var totalSales = g.Sum(i => i.TotalPrice + i.DiscountAmount);
                var totalReturnsValue = g.Sum(i => i.ReturnedQuantity * i.UnitPrice);
                var totalDiscounts = totalItemDiscount;

                return new ProductProfitRow(
                    ProductId:        g.Key,
                    ProductNameAr:    product.NameAr,
                    ProductNameEn:    product.NameEn ?? "",
                    SKU:              product.SKU,
                    CategoryName:     product.Category?.NameAr ?? "",
                    Image:            mainImage,
                    ListPrice:        product.Price,
                    DiscountPrice:    product.DiscountPrice,
                    CostPrice:        product.CostPrice,
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

        // --- Summary --------------------------------------------------------
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

    // ======================================================================
    // GET /api/profitability/summary
    // ملخص سريع للداشبورد
    // ======================================================================
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null, [FromQuery] int? branchId = null)
    {
        var now = TimeHelper.GetEgyptTime();
        var from = fromDate?.Date ?? new DateTime(now.Year, now.Month, 1);
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? now;

        int? isolatedBranchId = await User.HasViewAllBranchesAsync(HttpContext) ? branchId : User.GetBranchId();

        var itemsQ = _db.OrderItems
            .Include(i => i.Order)
            .Include(i => i.Product)
            .Where(i => i.Order.Status != OrderStatus.Cancelled
                     && i.Order.CreatedAt >= from && i.Order.CreatedAt <= to
                     && i.ProductId.HasValue);

        if (isolatedBranchId.HasValue)
        {
            itemsQ = itemsQ.Where(i => i.Order.BranchId == isolatedBranchId.Value);
        }

        var items = await itemsQ.ToListAsync();

        decimal totalNetRevenue = 0;
        decimal totalCost       = 0;

        var orderNumbers = items.Select(i => i.Order.OrderNumber).Distinct().ToList();
        var movements = await _db.InventoryMovements
            .Where(m => m.Type == InventoryMovementType.Sale && m.Reference != null && orderNumbers.Contains(m.Reference))
            .ToListAsync();

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
            
            var m = movements.FirstOrDefault(x => x.Reference == i.Order.OrderNumber && x.ProductId == i.ProductId && x.ProductVariantId == i.ProductVariantId);
            if (m != null) totalCost += m.UnitCost * netQty;
            else totalCost += (i.Product?.CostPrice ?? 0) * netQty;
        }

        var totalProfit = totalNetRevenue - totalCost;
        var margin      = totalNetRevenue > 0 ? Math.Round(totalProfit / totalNetRevenue * 100, 1) : 0;

        return Ok(new { totalRevenue = totalNetRevenue, totalCost, totalProfit, marginPct = margin, from, to });
    }

    // ======================================================================
    // EXCEL EXPORT
    // ======================================================================
    private IActionResult ExcelProfitability(
        List<ProductProfitRow> rows, dynamic summary, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();

        // --- Sheet 1: تفاصيل المنتجات ----------------------------
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
            ws.Cell(r,  5).Value = row.CostPrice.HasValue ? (XLCellValue)row.CostPrice.Value : (XLCellValue)"-";
            ws.Cell(r,  6).Value = row.UnitsSold;
            ws.Cell(r,  7).Value = row.ReturnedUnits;
            ws.Cell(r,  8).Value = row.NetUnits;
            ws.Cell(r,  9).Value = row.TotalSales;
            ws.Cell(r, 10).Value = row.TotalReturns;
            ws.Cell(r, 11).Value = row.TotalDiscounts;
            ws.Cell(r, 12).Value = row.NetRevenue;
            ws.Cell(r, 13).Value = row.TotalCost.HasValue ? (XLCellValue)row.TotalCost.Value : (XLCellValue)"-";
            ws.Cell(r, 14).Value = row.GrossProfit.HasValue ? (XLCellValue)row.GrossProfit.Value : (XLCellValue)"-";
            ws.Cell(r, 15).Value = row.MarginPct.HasValue ? (XLCellValue)row.MarginPct.Value : (XLCellValue)"-";

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
        if (r > 3) ws.Range(2, 1, r - 1, h.Length).SetAutoFilter();

        ws.Columns().AdjustToContents();

        // --- Sheet 2: الملخص التنفيذي ----------------------------
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
        Sportive.API.Utils.ExcelThemeHelper.ApplyElegantTheme(wb);
        wb.SaveAs(s); s.Position = 0;
        return new FileStreamResult(s,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            { FileDownloadName = name };
    }

    private static bool IsSoldItem(OrderItem? item)
    {
        if (item == null) return false;
        if (item.UnitPrice <= 0 || item.TotalPrice <= 0 || item.Quantity <= 0) return false;

        var nameAr = item.ProductNameAr ?? item.Product?.NameAr ?? "";
        var nameEn = item.ProductNameEn ?? item.Product?.NameEn ?? "";
        if (nameAr.Contains("خردة") || nameAr.Contains("بطارية قديمة") || nameAr.Contains("استبدال خردة"))
            return false;
        if (nameEn.IndexOf("trade-in", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        return true;
    }

// ======================================================================
    // GET /api/profitability/suppliers
    // ======================================================================
    [HttpGet("suppliers")]
    public async Task<IActionResult> GetSupplierProfitability(
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] int?      supplierId = null,
        [FromQuery] int?      branchId   = null,
        [FromQuery] string?   sortBy     = "profit",
        [FromQuery] bool      excel      = false)
    {
        var from = fromDate?.Date ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate?.Date ?? TimeHelper.GetEgyptTime().Date;
        
        var startRange = from.Date;
        var endRange   = to.Date.AddDays(1).AddTicks(-1);

        // 1. Fetch product-to-supplier mapping and unit purchase costs from PurchaseInvoiceItems
        var purchaseItemsQuery = _db.PurchaseInvoiceItems
            .Include(pii => pii.Invoice)
            .Where(pii => pii.ProductId.HasValue && pii.Invoice.Status != PurchaseInvoiceStatus.Cancelled);

        if (supplierId.HasValue)
        {
            purchaseItemsQuery = purchaseItemsQuery.Where(pii => pii.Invoice.SupplierId == supplierId.Value);
        }

        var rawPurchaseMap = await purchaseItemsQuery
            .OrderByDescending(pii => pii.Invoice.InvoiceDate)
            .Select(pii => new {
                ProductId = pii.ProductId!.Value,
                SupplierId = pii.Invoice.SupplierId,
                UnitCost = pii.UnitCost
            })
            .ToListAsync();

        // Map (SupplierId, ProductId) -> latest unit purchase cost from that supplier's purchase invoice
        var supplierProductCostDict = new Dictionary<(int SupplierId, int ProductId), decimal>();
        foreach (var p in rawPurchaseMap)
        {
            var key = (p.SupplierId, p.ProductId);
            if (!supplierProductCostDict.ContainsKey(key))
            {
                supplierProductCostDict[key] = p.UnitCost;
            }
        }

        var productSupplierMap = supplierId.HasValue
            ? rawPurchaseMap.Select(m => new { m.ProductId, m.SupplierId }).Distinct().ToList()
            : rawPurchaseMap.GroupBy(m => m.ProductId).Select(g => g.First()).Select(m => new { m.ProductId, m.SupplierId }).ToList();

        var relevantProductIds = productSupplierMap.Select(m => m.ProductId).Distinct().ToList();

        // 2. Query sales orders containing items of these products in the date range (Sold items only)
        var itemsQ = _db.OrderItems
            .Include(i => i.Order)
                .ThenInclude(o => o.Customer)
            .Include(i => i.Order)
                .ThenInclude(o => o.Items)
            .Include(i => i.Product)
            .Where(i => i.Order.Status != OrderStatus.Cancelled
                     && i.Order.CreatedAt >= startRange
                     && i.Order.CreatedAt <= endRange
                     && i.ProductId.HasValue
                     && relevantProductIds.Contains(i.ProductId.Value)
                     
                     && i.UnitPrice > 0
                     && i.TotalPrice > 0
                     && i.Quantity > 0);

        if (branchId.HasValue)
        {
            itemsQ = itemsQ.Where(i => i.Order.BranchId == branchId.Value);
        }

        var rawItems = await itemsQ.ToListAsync();
        var items = rawItems.Where(IsSoldItem).ToList();

        // Fetch purchases amount and discounts in period per supplier
        var purchasesInPeriodData = await _db.PurchaseInvoices
            .Where(pi => pi.InvoiceDate >= startRange && pi.InvoiceDate <= endRange && pi.Status != PurchaseInvoiceStatus.Cancelled)
            .GroupBy(pi => pi.SupplierId)
            .Select(g => new { 
                SupplierId = g.Key, 
                TotalPurchases = g.Sum(pi => pi.TotalAmount),
                TotalDiscount = g.Sum(pi => pi.DiscountAmount)
            })
            .ToListAsync();

        var purchasesInPeriod = purchasesInPeriodData.ToDictionary(x => x.SupplierId, x => x.TotalPurchases);
        var piDiscounts = purchasesInPeriodData.ToDictionary(x => x.SupplierId, x => x.TotalDiscount);

        // Fetch manual/settlement journal entry discounts for suppliers (Account 420102 / PurchaseDiscount)
        var mappedDiscountAcctId = await _db.AccountSystemMappings
            .Where(m => m.Key == MappingKeys.PurchaseDiscount)
            .Select(m => (int?)m.AccountId)
            .FirstOrDefaultAsync();

        var discountAccountIds = await _db.Accounts
            .Where(a => a.Code == "420102" || a.NameAr.Contains("خصم مكتسب") || (mappedDiscountAcctId.HasValue && a.Id == mappedDiscountAcctId.Value))
            .Select(a => a.Id)
            .ToListAsync();

        var journalDiscountsRaw = await _db.JournalLines
            .Include(jl => jl.JournalEntry)
            .Where(jl => discountAccountIds.Contains(jl.AccountId)
                      && jl.JournalEntry.EntryDate >= startRange
                      && jl.JournalEntry.EntryDate <= endRange
                      && jl.JournalEntry.Status == JournalEntryStatus.Posted
                      && jl.JournalEntry.Type != JournalEntryType.PurchaseInvoice
                      && jl.JournalEntry.Type != JournalEntryType.PurchaseReturn)
            .Select(jl => new {
                jl.JournalEntryId,
                DirectSupplierId = jl.SupplierId,
                Amount = jl.Credit - jl.Debit
            })
            .ToListAsync();

        var entriesNeedingSupplier = journalDiscountsRaw.Where(j => !j.DirectSupplierId.HasValue).Select(j => j.JournalEntryId).Distinct().ToList();
        var siblingSuppliers = entriesNeedingSupplier.Any()
            ? await _db.JournalLines
                .Where(jl => entriesNeedingSupplier.Contains(jl.JournalEntryId) && jl.SupplierId.HasValue)
                .GroupBy(jl => jl.JournalEntryId)
                .Select(g => new { EntryId = g.Key, SupplierId = g.First().SupplierId!.Value })
                .ToDictionaryAsync(x => x.EntryId, x => x.SupplierId)
            : new Dictionary<int, int>();

        var journalDiscountsBySupplier = new Dictionary<int, decimal>();
        foreach (var jd in journalDiscountsRaw)
        {
            var sId = jd.DirectSupplierId ?? siblingSuppliers.GetValueOrDefault(jd.JournalEntryId);
            if (sId > 0)
            {
                journalDiscountsBySupplier[sId] = journalDiscountsBySupplier.GetValueOrDefault(sId, 0m) + jd.Amount;
            }
        }

        // Fetch supplier entities (include all relevant suppliers)
        var targetSupplierIds = productSupplierMap.Select(m => m.SupplierId)
            .Union(purchasesInPeriod.Keys)
            .Union(journalDiscountsBySupplier.Keys)
            .Distinct()
            .ToList();

        var suppliers = await _db.Suppliers
            .Where(s => targetSupplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id);

        // 3. Compute profitability per supplier based on sales invoices
        var supplierRows = new List<SupplierProfitRow>();

        foreach (var suppId in targetSupplierIds)
        {
            if (!suppliers.TryGetValue(suppId, out var supp)) continue;

            var suppProdIdSet = productSupplierMap
                .Where(m => m.SupplierId == suppId)
                .Select(m => m.ProductId)
                .ToHashSet();

            var suppItems = items.Where(i => suppProdIdSet.Contains(i.ProductId!.Value)).ToList();

            // A. Compute Sales Invoices Breakdown for this Supplier
            var orderGroups = suppItems.GroupBy(i => i.OrderId);
            var supplierInvoices = orderGroups.Select(og =>
            {
                var firstItem = og.First();
                var order = firstItem.Order;
                var orderId = og.Key;
                var orderNumber = order.OrderNumber;
                var orderDate = order.CreatedAt;
                var customerName = order.Customer != null ? order.Customer.FullName : (!string.IsNullOrEmpty(order.SalesPersonId) ? "POS Walk-in" : "عميل نقدي");
                var source = order.Source == OrderSource.POS ? "POS" : "Website";

                decimal invSales = 0;
                decimal invDiscounts = 0;
                decimal invNetRev = 0;
                decimal invCost = 0;
                var itemNames = new List<string>();

                var orderSoldItemsTotal = order.Items.Where(IsSoldItem).Sum(x => x.TotalPrice);

                foreach (var i in og)
                {
                    var netQty = i.Quantity - i.ReturnedQuantity;
                    if (netQty < 0) netQty = 0;

                    var lineSubtotal = i.TotalPrice;
                    decimal lineOrderDiscountShare = 0;
                    if (orderSoldItemsTotal > 0 && order.DiscountAmount > 0)
                    {
                        var ratio = lineSubtotal / orderSoldItemsTotal;
                        lineOrderDiscountShare = ratio * order.DiscountAmount;
                    }

                    decimal qtyFactor = i.Quantity > 0 ? (decimal)netQty / i.Quantity : 0;
                    var netRevLine = (lineSubtotal * qtyFactor) - (lineOrderDiscountShare * qtyFactor);
                    invSales += lineSubtotal;
                    invDiscounts += lineOrderDiscountShare * qtyFactor;
                    invNetRev += netRevLine;

                    // Purchase invoice unit cost from this supplier
                    decimal unitCost = 0;
                    if (supplierProductCostDict.TryGetValue((suppId, i.ProductId!.Value), out var sCost) && sCost > 0)
                    {
                        unitCost = sCost;
                    }
                    else if (i.Product?.CostPrice.HasValue == true)
                    {
                        unitCost = i.Product.CostPrice.Value;
                    }

                    invCost += unitCost * netQty;

                    var pName = i.Product?.NameAr ?? i.Product?.NameEn ?? (!string.IsNullOrEmpty(i.ProductNameAr) ? i.ProductNameAr : $"#{i.ProductId}");
                    if (netQty > 0)
                    {
                        itemNames.Add($"{pName} ({netQty})");
                    }
                }

                var invProfit = invNetRev - invCost;
                var invMargin = invNetRev > 0 ? Math.Round((invProfit / invNetRev) * 100, 1) : 0m;

                return new SupplierInvoiceProfitDetail(
                    OrderId: orderId,
                    OrderNumber: orderNumber,
                    Date: orderDate,
                    CustomerName: customerName,
                    Source: source,
                    ItemsCount: og.Count(),
                    ItemsSummary: string.Join("، ", itemNames.Take(3)) + (itemNames.Count > 3 ? $" (+{itemNames.Count - 3})" : ""),
                    TotalSales: invSales,
                    TotalDiscounts: invDiscounts,
                    NetRevenue: invNetRev,
                    TotalCost: invCost,
                    GrossProfit: invProfit,
                    MarginPct: invMargin
                );
            })
            .OrderByDescending(inv => inv.Date)
            .ToList();

            // B. Compute Products Breakdown for this Supplier
            var productGroups = suppItems.GroupBy(i => i.ProductId!.Value);
            var relevantProfits = productGroups.Select(g =>
            {
                int prodId = g.Key;
                var prod = g.First().Product;
                var netUnits = g.Sum(i => i.Quantity - i.ReturnedQuantity);
                if (netUnits < 0) netUnits = 0;

                decimal totalNetRevenue = 0;
                decimal totalItemDiscount = 0;
                decimal totalSales = g.Sum(i => i.TotalPrice);
                decimal totalReturnsValue = g.Sum(i => i.ReturnedQuantity * i.UnitPrice);

                decimal unitCost = 0;
                if (supplierProductCostDict.TryGetValue((suppId, prodId), out var sCost) && sCost > 0)
                {
                    unitCost = sCost;
                }
                else if (prod?.CostPrice.HasValue == true)
                {
                    unitCost = prod.CostPrice.Value;
                }

                foreach (var i in g)
                {
                    var lineNetQty = (i.Quantity - i.ReturnedQuantity);
                    if (lineNetQty <= 0) continue;

                    var lineSubtotal = i.TotalPrice;
                    var orderSoldItemsTotal = i.Order.Items.Where(IsSoldItem).Sum(x => x.TotalPrice);
                    decimal lineOrderDiscountShare = 0;
                    if (orderSoldItemsTotal > 0 && i.Order.DiscountAmount > 0)
                    {
                        var ratio = lineSubtotal / orderSoldItemsTotal;
                        lineOrderDiscountShare = ratio * i.Order.DiscountAmount;
                    }

                    decimal qtyFactor = (decimal)lineNetQty / i.Quantity;
                    totalNetRevenue += (lineSubtotal * qtyFactor) - (lineOrderDiscountShare * qtyFactor);
                    totalItemDiscount += (lineOrderDiscountShare * qtyFactor);
                }

                decimal totalCost = unitCost * netUnits;
                var grossProfit = totalNetRevenue - totalCost;
                var marginPct = totalNetRevenue > 0 ? Math.Round((grossProfit / totalNetRevenue) * 100, 1) : 0m;

                return new SupplierProductProfitDetail(
                    ProductId: prodId,
                    ProductName: !string.IsNullOrEmpty(prod?.NameAr) ? prod.NameAr : (!string.IsNullOrEmpty(prod?.NameEn) ? prod.NameEn : (!string.IsNullOrEmpty(g.First().ProductNameAr) ? g.First().ProductNameAr : $"Product #{prodId}")),
                    Sku: prod?.SKU ?? g.First().SKU,
                    UnitsSold: g.Sum(i => i.Quantity),
                    ReturnedUnits: g.Sum(i => i.ReturnedQuantity),
                    NetUnits: netUnits,
                    TotalSales: totalSales,
                    TotalReturns: totalReturnsValue,
                    TotalDiscounts: totalItemDiscount,
                    NetRevenue: totalNetRevenue,
                    TotalCost: totalCost,
                    GrossProfit: grossProfit,
                    MarginPct: marginPct
                );
            })
            .OrderByDescending(p => p.GrossProfit)
            .ToList();

            // C. Totals for this Supplier
            int unitsSold = relevantProfits.Sum(p => p.UnitsSold);
            int returnedUnits = relevantProfits.Sum(p => p.ReturnedUnits);
            int netUnits = relevantProfits.Sum(p => p.NetUnits);
            decimal totalSales = relevantProfits.Sum(p => p.TotalSales);
            decimal totalReturns = relevantProfits.Sum(p => p.TotalReturns);
            decimal totalDiscounts = relevantProfits.Sum(p => p.TotalDiscounts);
            decimal netRevenue = relevantProfits.Sum(p => p.NetRevenue);
            decimal totalCost = relevantProfits.Sum(p => p.TotalCost);
            decimal earnedDiscount = piDiscounts.GetValueOrDefault(suppId, 0m) + journalDiscountsBySupplier.GetValueOrDefault(suppId, 0m);
            decimal grossProfit = netRevenue - (totalCost - earnedDiscount);
            decimal marginPct = netRevenue > 0 ? Math.Round((grossProfit / netRevenue) * 100, 1) : 0m;
            decimal purchasesPeriod = purchasesInPeriod.GetValueOrDefault(suppId, 0m);
            decimal currentBalance = supp.Balance;
            decimal? sellThroughRate = purchasesPeriod > 0 ? Math.Round((totalCost / purchasesPeriod) * 100, 1) : null;

            supplierRows.Add(new SupplierProfitRow(
                SupplierId: suppId,
                SupplierName: supp.Name,
                CompanyName: supp.CompanyName ?? "",
                Phone: supp.Phone ?? "",
                ProductsCount: suppProdIdSet.Count,
                UnitsSold: unitsSold,
                ReturnedUnits: returnedUnits,
                NetUnits: netUnits,
                TotalSales: totalSales,
                TotalReturns: totalReturns,
                TotalDiscounts: totalDiscounts,
                NetRevenue: netRevenue,
                TotalCost: totalCost,
                EarnedDiscount: earnedDiscount,
                GrossProfit: grossProfit,
                MarginPct: marginPct,
                PurchasesInPeriod: purchasesPeriod,
                CurrentBalance: currentBalance,
                SellThroughRate: sellThroughRate,
                Products: relevantProfits,
                Invoices: supplierInvoices
            ));
        }

        // Sorting
        supplierRows = sortBy switch
        {
            "revenue" => supplierRows.OrderByDescending(r => r.NetRevenue).ToList(),
            "margin"  => supplierRows.OrderByDescending(r => r.MarginPct).ToList(),
            "units"   => supplierRows.OrderByDescending(r => r.NetUnits).ToList(),
            _         => supplierRows.OrderByDescending(r => r.GrossProfit).ToList(),
        };

        var summary = new
        {
            totalSuppliers    = supplierRows.Count,
            totalSales        = supplierRows.Sum(r => r.TotalSales),
            totalReturns      = supplierRows.Sum(r => r.TotalReturns),
            totalDiscounts    = supplierRows.Sum(r => r.TotalDiscounts),
            totalNetRevenue   = supplierRows.Sum(r => r.NetRevenue),
            totalCost         = supplierRows.Sum(r => r.TotalCost),
            totalEarnedDiscount = supplierRows.Sum(r => r.EarnedDiscount),
            totalGrossProfit  = supplierRows.Sum(r => r.GrossProfit),
            overallMargin     = supplierRows.Sum(r => r.NetRevenue) > 0
                ? Math.Round(supplierRows.Sum(r => r.GrossProfit) / supplierRows.Sum(r => r.NetRevenue) * 100, 1)
                : 0m,
            totalUnitsSold    = supplierRows.Sum(r => r.UnitsSold),
            totalReturned     = supplierRows.Sum(r => r.ReturnedUnits),
            totalPurchases    = supplierRows.Sum(r => r.PurchasesInPeriod),
            topProfitSupplier = supplierRows.OrderByDescending(r => r.GrossProfit).FirstOrDefault()?.SupplierName,
            topProfitValue    = supplierRows.OrderByDescending(r => r.GrossProfit).FirstOrDefault()?.GrossProfit,
            topMarginSupplier = supplierRows.Where(r => r.NetRevenue > 0).OrderByDescending(r => r.MarginPct).FirstOrDefault()?.SupplierName,
            topMarginPct      = supplierRows.Where(r => r.NetRevenue > 0).OrderByDescending(r => r.MarginPct).FirstOrDefault()?.MarginPct,
        };

        if (excel) return ExcelSupplierProfitability(supplierRows, summary, from, to);

        return Ok(new { from, to, summary, suppliers = supplierRows });
    }

    private IActionResult ExcelSupplierProfitability(
        List<SupplierProfitRow> rows, dynamic summary, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();

        var ws = wb.Worksheets.Add("ربحية الموردين");
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = $"تقرير ربحية الموردين — من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";
        ws.Cell(1, 1).Style.Font.Bold     = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Range(1, 1, 1, 16).Merge();

        string[] h = {
            "اسم المورد", "الشركة", "الهاتف", "عدد الأصناف",
            "قطع مباعة", "قطع مرتجعة", "صافي القطع",
            "إجمالي المبيعات", "المرتجعات", "الخصومات", "صافي الإيراد",
            "التكلفة", "الخصم المكتسب", "إجمالي الربح", "هامش الربح %", "مشتريات الفترة"
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
            ws.Cell(r,  1).Value = row.SupplierName;
            ws.Cell(r,  2).Value = row.CompanyName;
            ws.Cell(r,  3).Value = row.Phone;
            ws.Cell(r,  4).Value = row.ProductsCount;
            ws.Cell(r,  5).Value = row.UnitsSold;
            ws.Cell(r,  6).Value = row.ReturnedUnits;
            ws.Cell(r,  7).Value = row.NetUnits;
            ws.Cell(r,  8).Value = row.TotalSales;
            ws.Cell(r,  9).Value = row.TotalReturns;
            ws.Cell(r, 10).Value = row.TotalDiscounts;
            ws.Cell(r, 11).Value = row.NetRevenue;
            ws.Cell(r, 12).Value = row.TotalCost;
            ws.Cell(r, 13).Value = row.EarnedDiscount;
            ws.Cell(r, 14).Value = row.GrossProfit;
            ws.Cell(r, 15).Value = row.MarginPct;
            ws.Cell(r, 16).Value = row.PurchasesInPeriod;

            foreach (int col in new[] { 8, 9, 10, 11, 12, 13, 14, 16 })
                if (ws.Cell(r, col).Value.IsNumber)
                    ws.Cell(r, col).Style.NumberFormat.Format = "#,##0.00";

            if (ws.Cell(r, 15).Value.IsNumber)
                ws.Cell(r, 15).Style.NumberFormat.Format = "0.0\"%\"";

            if (row.MarginPct >= 30)
                ws.Cell(r, 15).Style.Fill.BackgroundColor = XLColor.FromHtml("#e8f5e9");
            else if (row.MarginPct >= 10)
                ws.Cell(r, 15).Style.Fill.BackgroundColor = XLColor.FromHtml("#fff8e1");
            else
                ws.Cell(r, 15).Style.Fill.BackgroundColor = XLColor.FromHtml("#ffebee");

            r++;
        }

        // Totals row
        ws.Cell(r, 1).Value = "الإجمالي";
        ws.Cell(r, 1).Style.Font.Bold = true;
        ws.Cell(r, 5).Value = rows.Sum(x => x.UnitsSold);
        ws.Cell(r, 6).Value = rows.Sum(x => x.ReturnedUnits);
        ws.Cell(r, 7).Value = rows.Sum(x => x.NetUnits);
        ws.Cell(r, 8).Value = rows.Sum(x => x.TotalSales);
        ws.Cell(r, 9).Value = rows.Sum(x => x.TotalReturns);
        ws.Cell(r, 10).Value = rows.Sum(x => x.TotalDiscounts);
        ws.Cell(r, 11).Value = rows.Sum(x => x.NetRevenue);
        ws.Cell(r, 12).Value = rows.Sum(x => x.TotalCost);
        ws.Cell(r, 13).Value = rows.Sum(x => x.EarnedDiscount);
        ws.Cell(r, 14).Value = rows.Sum(x => x.GrossProfit);
        ws.Cell(r, 16).Value = rows.Sum(x => x.PurchasesInPeriod);
        for (int col = 5; col <= 16; col++)
        {
            ws.Cell(r, col).Style.Font.Bold = true;
            if (col >= 8 && col != 15) ws.Cell(r, col).Style.NumberFormat.Format = "#,##0.00";
        }
        ws.Row(r).Style.Fill.BackgroundColor = XLColor.FromHtml("#e8f5e9");
        if (r > 3) ws.Range(2, 1, r - 1, h.Length).SetAutoFilter();

        ws.Columns().AdjustToContents();

        return ExcelFile(wb, $"supplier_profitability_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }

    }

// --- Report DTO -------------------------------------------------------
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

public record SupplierProductProfitDetail(
    int ProductId,
    string ProductName,
    string? Sku,
    int UnitsSold,
    int ReturnedUnits,
    int NetUnits,
    decimal TotalSales,
    decimal TotalReturns,
    decimal TotalDiscounts,
    decimal NetRevenue,
    decimal TotalCost,
    decimal GrossProfit,
    decimal MarginPct
);

public record SupplierInvoiceProfitDetail(
    int OrderId,
    string OrderNumber,
    DateTime Date,
    string CustomerName,
    string Source,
    int ItemsCount,
    string ItemsSummary,
    decimal TotalSales,
    decimal TotalDiscounts,
    decimal NetRevenue,
    decimal TotalCost,
    decimal GrossProfit,
    decimal MarginPct
);

public record SupplierProfitRow(
    int     SupplierId,
    string  SupplierName,
    string  CompanyName,
    string  Phone,
    int     ProductsCount,
    int     UnitsSold,
    int     ReturnedUnits,
    int     NetUnits,
    decimal TotalSales,
    decimal TotalReturns,
    decimal TotalDiscounts,
    decimal NetRevenue,
    decimal TotalCost,
    decimal EarnedDiscount,
    decimal GrossProfit,
    decimal MarginPct,
    decimal PurchasesInPeriod,
    decimal CurrentBalance = 0,
    decimal? SellThroughRate = null,
    List<SupplierProductProfitDetail>? Products = null,
    List<SupplierInvoiceProfitDetail>? Invoices = null
);

