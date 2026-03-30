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
public class ImportController : ControllerBase
{
    private readonly AppDbContext _db;

    public ImportController(AppDbContext db) => _db = db;

    // ── TEMPLATE DOWNLOAD ────────────────────────────────────
    // GET /api/import/template
    [HttpGet("template")]
    public IActionResult GetTemplate()
    {
        using var wb = new XLWorkbook();

        // Sheet 1: Products
        var ws1 = wb.Worksheets.Add("المنتجات");
        ws1.RightToLeft = true;

        var headers1 = new[] {
            "الاسم عربي *","الاسم انجليزي *","كود الفئة *","الكود SKU *",
            "السعر *","سعر الخصم","العلامة التجارية","الوصف عربي","الوصف انجليزي",
            "مميز (نعم/لا)"
        };
        for (int c = 0; c < headers1.Length; c++)
        {
            var cell = ws1.Cell(1, c+1);
            cell.Value = headers1[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
            cell.Style.Font.FontColor = XLColor.White;
        }

        // Sample row
        ws1.Cell(2,1).Value = "تيشرت رياضي أزرق";
        ws1.Cell(2,2).Value = "Blue Sports T-Shirt";
        ws1.Cell(2,3).Value = "1";
        ws1.Cell(2,4).Value = "TS-001";
        ws1.Cell(2,5).Value = 299;
        ws1.Cell(2,6).Value = 249;
        ws1.Cell(2,7).Value = "Nike";
        ws1.Cell(2,8).Value = "تيشرت رياضي عالي الجودة";
        ws1.Cell(2,9).Value = "High quality sports t-shirt";
        ws1.Cell(2,10).Value = "لا";
        ws1.Row(2).Style.Font.FontColor = XLColor.Gray;
        ws1.Columns().AdjustToContents();

        // Sheet 2: Variants
        var ws2 = wb.Worksheets.Add("المقاسات");
        ws2.RightToLeft = true;

        var headers2 = new[] { "الكود SKU *","المقاس","اللون (English)","اللون (عربي)","المخزون *","فارق السعر" };
        for (int c = 0; c < headers2.Length; c++)
        {
            var cell = ws2.Cell(1, c+1);
            cell.Value = headers2[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f3460");
            cell.Style.Font.FontColor = XLColor.White;
        }

        // Sample rows
        string[][] samples = {
            ["TS-001","S","Blue","أزرق","10","0"],
            ["TS-001","M","Blue","أزرق","15","0"],
            ["TS-001","L","Blue","أزرق","8","0"],
            ["TS-001","XL","Blue","أزرق","5","10"],
        };
        for (int i = 0; i < samples.Length; i++)
        {
            for (int j = 0; j < samples[i].Length; j++)
                ws2.Cell(i+2, j+1).Value = samples[i][j];
            ws2.Row(i+2).Style.Font.FontColor = XLColor.Gray;
        }
        ws2.Columns().AdjustToContents();

        // Sheet 3: Categories list
        var ws3 = wb.Worksheets.Add("الفئات المتاحة");
        ws3.Cell(1,1).Value = "كود الفئة";
        ws3.Cell(1,2).Value = "اسم الفئة";
        ws3.Cell(1,1).Style.Font.Bold = true;
        ws3.Cell(1,2).Style.Font.Bold = true;
        // Will be populated dynamically — just show headers
        ws3.Cell(2,1).Value = "(أدخل كود الفئة من هذه القائمة في شيت المنتجات)";
        ws3.Columns().AdjustToContents();

        var stream = new MemoryStream();
        wb.SaveAs(stream); stream.Position = 0;

        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "products_import_template.xlsx");
    }

    // ── IMPORT PRODUCTS ───────────────────────────────────────
    // POST /api/import/products
    [HttpPost("products")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB
    public async Task<IActionResult> ImportProducts(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "لم يتم رفع ملف" });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext != ".xlsx" && ext != ".xls")
            return BadRequest(new { message = "يجب أن يكون الملف بصيغة Excel (.xlsx)" });

        var result = new ImportResult();

        try
        {
            using var stream = file.OpenReadStream();
            using var wb     = new XLWorkbook(stream);

            // ── Sheet 1: Products ──────────────────────────────
            if (!wb.TryGetWorksheet("المنتجات", out var ws1))
                ws1 = wb.Worksheets.First();

            var lastRow = ws1.LastRowUsed()?.RowNumber() ?? 1;
            var categories = await _db.Categories.ToListAsync();
            var existingSkus = await _db.Products.Select(p => p.SKU).ToHashSetAsync();

            for (int r = 2; r <= lastRow; r++)
            {
                var nameAr = ws1.Cell(r, 1).GetString().Trim();
                var nameEn = ws1.Cell(r, 2).GetString().Trim();
                var catStr = ws1.Cell(r, 3).GetString().Trim();
                var sku    = ws1.Cell(r, 4).GetString().Trim();
                var priceStr = ws1.Cell(r, 5).GetString().Trim();

                if (string.IsNullOrEmpty(nameAr) && string.IsNullOrEmpty(sku)) continue;

                if (string.IsNullOrEmpty(nameAr) || string.IsNullOrEmpty(sku) || string.IsNullOrEmpty(priceStr))
                {
                    result.Errors.Add($"صف {r}: بيانات ناقصة (الاسم أو الكود أو السعر)");
                    continue;
                }

                if (!decimal.TryParse(priceStr, out var price) || price <= 0)
                {
                    result.Errors.Add($"صف {r}: السعر غير صحيح '{priceStr}'");
                    continue;
                }

                if (existingSkus.Contains(sku))
                {
                    result.Skipped.Add($"صف {r}: الكود '{sku}' موجود مسبقاً — تم تخطيه");
                    continue;
                }

                int? categoryId = null;
                if (int.TryParse(catStr, out var catId))
                {
                    var cat = categories.FirstOrDefault(c => c.Id == catId);
                    if (cat != null) categoryId = cat.Id;
                }

                decimal? discountPrice = null;
                var discStr = ws1.Cell(r, 6).GetString().Trim();
                if (!string.IsNullOrEmpty(discStr) && decimal.TryParse(discStr, out var disc) && disc > 0)
                    discountPrice = disc;

                var product = new Product
                {
                    NameAr        = nameAr,
                    NameEn        = string.IsNullOrEmpty(nameEn) ? nameAr : nameEn,
                    CategoryId    = categoryId ?? categories.FirstOrDefault()?.Id ?? 1,
                    SKU           = sku,
                    Price         = price,
                    DiscountPrice = discountPrice,
                    Brand         = ws1.Cell(r, 7).GetString().Trim().NullIfEmpty(),
                    DescriptionAr = ws1.Cell(r, 8).GetString().Trim().NullIfEmpty(),
                    DescriptionEn = ws1.Cell(r, 9).GetString().Trim().NullIfEmpty(),
                    IsFeatured    = ws1.Cell(r, 10).GetString().Contains("نعم"),
                    Status        = ProductStatus.Active,
                    CreatedAt     = DateTime.UtcNow,
                };

                _db.Products.Add(product);
                existingSkus.Add(sku);
                result.Added++;
            }

            await _db.SaveChangesAsync();

            // ── Sheet 2: Variants ──────────────────────────────
            if (wb.TryGetWorksheet("المقاسات", out var ws2))
            {
                var lastRow2 = ws2.LastRowUsed()?.RowNumber() ?? 1;
                var products = await _db.Products.ToDictionaryAsync(p => p.SKU);

                for (int r = 2; r <= lastRow2; r++)
                {
                    var sku      = ws2.Cell(r, 1).GetString().Trim();
                    var size     = ws2.Cell(r, 2).GetString().Trim().NullIfEmpty();
                    var color    = ws2.Cell(r, 3).GetString().Trim().NullIfEmpty();
                    var colorAr  = ws2.Cell(r, 4).GetString().Trim().NullIfEmpty();
                    var stockStr = ws2.Cell(r, 5).GetString().Trim();

                    if (string.IsNullOrEmpty(sku)) continue;

                    if (!products.TryGetValue(sku, out var product))
                    {
                        result.Errors.Add($"مقاسات صف {r}: الكود '{sku}' غير موجود");
                        continue;
                    }

                    if (!int.TryParse(stockStr, out var stock))
                    {
                        result.Errors.Add($"مقاسات صف {r}: المخزون غير صحيح");
                        continue;
                    }

                    decimal? adj = null;
                    var adjStr = ws2.Cell(r, 6).GetString().Trim();
                    if (!string.IsNullOrEmpty(adjStr) && decimal.TryParse(adjStr, out var a) && a != 0)
                        adj = a;

                    _db.ProductVariants.Add(new ProductVariant
                    {
                        ProductId       = product.Id,
                        Size            = size,
                        Color           = color,
                        ColorAr         = colorAr,
                        StockQuantity   = stock,
                        PriceAdjustment = adj,
                        CreatedAt       = DateTime.UtcNow,
                    });
                    result.VariantsAdded++;
                }

                await _db.SaveChangesAsync();

                // Recalculate Total Stock for all products that have variants
                var productsWithVariants = await _db.Products.Include(p => p.Variants)
                    .Where(p => p.Variants.Any(v => !v.IsDeleted)).ToListAsync();
                foreach (var p in productsWithVariants)
                {
                    p.TotalStock = p.Variants.Where(v => !v.IsDeleted).Sum(v => v.StockQuantity);
                }
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"خطأ في قراءة الملف: {ex.Message}" });
        }

        return Ok(new {
            message = $"تم الاستيراد بنجاح",
            added          = result.Added,
            variantsAdded  = result.VariantsAdded,
            skipped        = result.Skipped.Count,
            errors         = result.Errors.Count,
            details        = result
        });
    }

    // ── INVENTORY TEMPLATE ────────────────────────────────────
    // GET /api/import/inventory-template
    [HttpGet("inventory-template")]
    public async Task<IActionResult> GetInventoryTemplate()
    {
        using var wb = new XLWorkbook();

        // Sheet 1: Stock Update
        var ws = wb.Worksheets.Add("تحديث المخزون");
        ws.RightToLeft = true;

        var headers = new[] { "الكود (SKU) *", "اسم المنتج", "المقاس", "اللون", "الكمية الجديدة *" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Add instruction row
        ws.Cell(2, 1).Value = "(أدخل SKU المنتج أو المتغير والكمية الفعلية)";
        ws.Row(2).Style.Font.FontColor = XLColor.Gray;
        ws.Row(2).Style.Font.Italic = true;

        // Populate with existing products + variants as reference
        var products = await _db.Products
            .Include(p => p.Variants)
            .OrderBy(p => p.NameAr)
            .Take(200)
            .ToListAsync();

        int row = 3;
        foreach (var p in products)
        {
            if (!p.Variants.Any(v => !v.IsDeleted))
            {
                // Product-level (no variants)
                ws.Cell(row, 1).Value = p.SKU;
                ws.Cell(row, 2).Value = p.NameAr;
                ws.Cell(row, 3).Value = "";
                ws.Cell(row, 4).Value = "";
                ws.Cell(row, 5).Value = p.TotalStock;
                ws.Row(row).Style.Font.FontColor = XLColor.DarkGray;
                row++;
            }
            else
            {
                foreach (var v in p.Variants.Where(v => !v.IsDeleted))
                {
                    var varSku = p.SKU;
                    ws.Cell(row, 1).Value = varSku;
                    ws.Cell(row, 2).Value = p.NameAr;
                    ws.Cell(row, 3).Value = v.Size ?? "";
                    ws.Cell(row, 4).Value = v.Color ?? "";
                    ws.Cell(row, 5).Value = v.StockQuantity;
                    ws.Row(row).Style.Font.FontColor = XLColor.DarkGray;
                    row++;
                }
            }
        }

        ws.Columns().AdjustToContents();

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;

        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "inventory_import_template.xlsx");
    }

    // ── IMPORT INVENTORY (STOCK UPDATE ONLY) ──────────────────
    // POST /api/import/inventory
    [HttpPost("inventory")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ImportInventory(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "لم يتم رفع ملف" });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext != ".xlsx" && ext != ".xls")
            return BadRequest(new { message = "يجب أن يكون الملف بصيغة Excel (.xlsx)" });

        int updated = 0;
        var skipped = new List<string>();
        var errors  = new List<string>();

        try
        {
            using var stream = file.OpenReadStream();
            using var wb     = new XLWorkbook(stream);

            if (!wb.TryGetWorksheet("تحديث المخزون", out var ws))
                ws = wb.Worksheets.First();

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            // Load all products with their variants
            var allProducts = await _db.Products
                .Include(p => p.Variants.Where(v => !v.IsDeleted))
                .ToListAsync();

            // Build lookup: productSKU -> product
            var productBySku = allProducts.ToDictionary(p => p.SKU, StringComparer.OrdinalIgnoreCase);

            for (int r = 2; r <= lastRow; r++)
            {
                var sku    = ws.Cell(r, 1).GetString().Trim();
                var size   = ws.Cell(r, 3).GetString().Trim().ToLower();
                var color  = ws.Cell(r, 4).GetString().Trim().ToLower();
                var qtyStr = ws.Cell(r, 5).GetString().Trim();

                // Skip empty or instruction rows
                if (string.IsNullOrEmpty(sku) || sku.StartsWith("(")) continue;

                if (!int.TryParse(qtyStr, out var qty) || qty < 0)
                {
                    errors.Add($"صف {r}: الكمية غير صحيحة للكود '{sku}'");
                    continue;
                }

                if (!productBySku.TryGetValue(sku, out var product))
                {
                    skipped.Add($"صف {r}: الكود '{sku}' غير موجود في النظام — تم تخطيه");
                    continue;
                }

                var activeVariants = product.Variants.Where(v => !v.IsDeleted).ToList();

                if (!activeVariants.Any())
                {
                    // Product without variants — update directly
                    product.TotalStock = qty;
                    updated++;
                }
                else if (!string.IsNullOrEmpty(size) || !string.IsNullOrEmpty(color))
                {
                    // Match by size and/or color
                    var variant = activeVariants.FirstOrDefault(v =>
                        (string.IsNullOrEmpty(size)  || (v.Size  ?? "").ToLower() == size) &&
                        (string.IsNullOrEmpty(color) || (v.Color ?? "").ToLower() == color));

                    if (variant == null)
                    {
                        skipped.Add($"صف {r}: لم يُعثر على مقاس/لون '{size}/{color}' للكود '{sku}'");
                        continue;
                    }

                    variant.StockQuantity = qty;

                    // Recalculate product total stock
                    product.TotalStock = activeVariants.Sum(v => v.StockQuantity);
                    updated++;
                }
                else
                {
                    // SKU has variants but no size/color specified — skip with helpful message
                    skipped.Add($"صف {r}: الكود '{sku}' له مقاسات — حدد المقاس واللون في العمودين C وD");
                    continue;
                }
            }

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"خطأ في قراءة الملف: {ex.Message}" });
        }

        return Ok(new
        {
            message  = $"تم تحديث مخزون {updated} صف بنجاح",
            updated,
            skipped  = skipped.Count,
            errors   = errors.Count,
            details  = new { skipped, errors }
        });
    }

    private class ImportResult
    {
        public int Added         { get; set; }
        public int VariantsAdded { get; set; }
        public List<string> Skipped { get; set; } = new();
        public List<string> Errors  { get; set; } = new();
    }
}

// Extension
internal static class StringExtensions
{
    public static string? NullIfEmpty(this string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;
}
