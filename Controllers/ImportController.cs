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
[Authorize(Roles = "Admin")]
public class ImportController : ControllerBase
{
    private readonly AppDbContext _db;
    

    public ImportController(AppDbContext db) => _db = db;

    // ── TEMPLATE DOWNLOAD ────────────────────────────────────
    // GET /api/import/template
    // ── TEMPLATE DOWNLOAD ────────────────────────────────────
    // GET /api/import/template
    [HttpGet("template")]
    public async Task<IActionResult> GetTemplate()
    {
        var mainCategories = await _db.Categories.Where(c => c.ParentId == null).Select(c => c.NameAr).ToListAsync();
        var subCategories = await _db.Categories.Where(c => c.ParentId != null).Select(c => c.NameAr).ToListAsync();
        var brands     = await _db.Brands.Select(b => b.NameAr).ToListAsync();
        var units      = await _db.ProductUnits.Select(u => u.NameAr).ToListAsync();
        
        var sizes    = await _db.ProductVariants.Where(v => !string.IsNullOrEmpty(v.Size)).Select(v => v.Size!).Distinct().ToListAsync();
        var colorEns = await _db.ProductVariants.Where(v => !string.IsNullOrEmpty(v.Color)).Select(v => v.Color!).Distinct().ToListAsync();
        var colorArs = await _db.ProductVariants.Where(v => !string.IsNullOrEmpty(v.ColorAr)).Select(v => v.ColorAr!).Distinct().ToListAsync();

        using var wb = new XLWorkbook();
        var wsL = wb.Worksheets.Add("Lists");
        wsL.Hide();
        
        void FillCol(int col, List<string> items) {
            for (int i = 0; i < items.Count; i++) wsL.Cell(i + 1, col).Value = items[i];
        }
        FillCol(1, mainCategories); 
        FillCol(2, subCategories);
        FillCol(3, brands);     
        FillCol(4, units);      
        FillCol(5, sizes);      
        FillCol(6, colorEns);   
        FillCol(7, colorArs);   
        FillCol(8, new List<string> { "نعم", "لا" });

        var mCatRange  = wsL.Range(1, 1, Math.Max(1, mainCategories.Count), 1);
        var sCatRange  = wsL.Range(1, 2, Math.Max(1, subCategories.Count), 2);
        var brandRange = wsL.Range(1, 3, Math.Max(1, brands.Count), 3);
        var unitRange  = wsL.Range(1, 4, Math.Max(1, units.Count), 4);
        var sizeRange  = wsL.Range(1, 5, Math.Max(1, sizes.Count), 5);
        var cEnRange   = wsL.Range(1, 6, Math.Max(1, colorEns.Count), 6);
        var cArRange   = wsL.Range(1, 7, Math.Max(1, colorArs.Count), 7);
        var featRange  = wsL.Range(1, 8, 2, 8);

        var ws1 = wb.Worksheets.Add("المنتجات والمقاسات");
        ws1.RightToLeft = true;

        var headers1 = new[] {
            "الاسم عربي *","الاسم انجليزي","التصنيف الأساسي *","التصنيف الفرعي","الكود SKU *",
            "السعر *","سعر الخصم","سعر التكلفة","الماركة","الوحدة",
            "المقاس","اللون (English)","اللون (عربي)","المخزون *","فارق السعر للمقاس",
            "مميز (نعم/لا)","الوصف عربي","الوصف انجليزي"
        };
        for (int c = 0; c < headers1.Length; c++)
        {
            var cell = ws1.Cell(1, c+1);
            cell.Value = headers1[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
            cell.Style.Font.FontColor = XLColor.White;
        }

        ws1.Column(5).Style.NumberFormat.Format = "@"; // Force text for SKU

        for (int r = 2; r <= 1000; r++)
        {
            ws1.Cell(r, 3).CreateDataValidation().List(mCatRange, true);
            ws1.Cell(r, 4).CreateDataValidation().List(sCatRange, true);
            ws1.Cell(r, 9).CreateDataValidation().List(brandRange, true);
            ws1.Cell(r, 10).CreateDataValidation().List(unitRange, true);
            ws1.Cell(r, 11).CreateDataValidation().List(sizeRange, true);
            ws1.Cell(r, 12).CreateDataValidation().List(cEnRange, true);
            ws1.Cell(r, 13).CreateDataValidation().List(cArRange, true);
            ws1.Cell(r, 16).CreateDataValidation().List(featRange, true);
        }

        // Sample row 1 (Variant 1)
        ws1.Cell(2,1).Value = "تيشرت رياضي";
        ws1.Cell(2,2).Value = "Sports T-Shirt";
        ws1.Cell(2,3).Value = mainCategories.FirstOrDefault() ?? "ملابس";
        ws1.Cell(2,5).Value = "TS-001";
        ws1.Cell(2,6).Value = 299;
        ws1.Cell(2,8).Value = 200; // Cost
        ws1.Cell(2,9).Value = brands.FirstOrDefault() ?? "Nike";
        ws1.Cell(2,10).Value = units.FirstOrDefault() ?? "قطعة";
        ws1.Cell(2,11).Value = "M";
        ws1.Cell(2,12).Value = "Blue";
        ws1.Cell(2,13).Value = "أزرق";
        ws1.Cell(2,14).Value = 10;
        ws1.Cell(2,15).Value = 0;
        ws1.Cell(2,16).Value = "لا";
        ws1.Row(2).Style.Font.FontColor = XLColor.Gray;

        // Sample row 2 (Variant 2 - Same Product)
        ws1.Cell(3,1).Value = "تيشرت رياضي";
        ws1.Cell(3,5).Value = "TS-001";
        ws1.Cell(3,6).Value = 299;
        ws1.Cell(3,11).Value = "L";
        ws1.Cell(3,12).Value = "Blue";
        ws1.Cell(3,13).Value = "أزرق";
        ws1.Cell(3,14).Value = 5;
        ws1.Row(3).Style.Font.FontColor = XLColor.Gray;

        ws1.Columns().AdjustToContents();

        var stream = new MemoryStream();
        wb.SaveAs(stream); stream.Position = 0;

        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "products_import_template_v2.xlsx");
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

            var ws = wb.Worksheets.FirstOrDefault();
            if (ws == null) throw new Exception("الملف فارغ");

            var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var firstRow = ws.Row(1);
            for (int c = 1; c <= ws.LastColumnUsed()?.ColumnNumber(); c++)
            {
                var hText = firstRow.Cell(c).GetString().Trim().Replace("*", "").Trim();
                if (!string.IsNullOrEmpty(hText) && !headers.ContainsKey(hText))
                    headers[hText] = c;
            }

            int GetCol(params string[] aliases) {
                foreach (var a in aliases) if (headers.TryGetValue(a, out var idx)) return idx;
                return -1;
            }

            // Mapping columns
            int colNameAr   = GetCol("الاسم عربي", "اسم المنتج");
            int colNameEn   = GetCol("الاسم انجليزي", "الاسم انجليزي");
            int colMainCat  = GetCol("التصنيف الأساسي", "الفئة", "كود الفئة");
            int colSubCat   = GetCol("التصنيف الفرعي");
            int colSku      = GetCol("الكود SKU", "كود SKU", "sku");
            int colPrice    = GetCol("السعر", "سعر البيع");
            int colDisc     = GetCol("سعر الخصم", "الخصم");
            int colCost     = GetCol("سعر التكلفة", "التكلفة");
            int colBrand    = GetCol("العلامة التجارية", "الماركة", "Brand");
            int colUnit     = GetCol("الوحدة", "وحدة القياس");
            int colSize     = GetCol("المقاس");
            int colColorEn  = GetCol("اللون (English)", "اللون English");
            int colColorAr  = GetCol("اللون (عربي)", "اللون عربي");
            int colStock    = GetCol("المخزون");
            int colAdj      = GetCol("فارق السعر", "فارق السعر للمقاس");
            int colFeat     = GetCol("مميز (نعم/لا)", "مميز");
            int colDescAr   = GetCol("الوصف عربي");
            int colDescEn   = GetCol("الوصف انجليزي");

            if (colSku == -1 || (colNameAr == -1 && colPrice == -1))
                return BadRequest(new { message = "تعذر التعرف على أعمدة الملف. يرجى استخدام القالب المحمل من الموقع." });

            var categories   = await _db.Categories.ToListAsync();
            var brands       = await _db.Brands.ToListAsync();
            var units        = await _db.ProductUnits.ToListAsync();
            var existingSkus = await _db.Products.Select(p => p.SKU).ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

            var productsDict = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            for (int r = 2; r <= lastRow; r++)
            {
                string GetVal(int col) => col != -1 ? ws.Cell(r, col).GetString().Trim() : "";

                var sku = GetVal(colSku);
                if (string.IsNullOrEmpty(sku)) continue;

                if (existingSkus.Contains(sku))
                {
                    if (!result.Skipped.Any(s => s.Contains($"الكود '{sku}' موجود")))
                        result.Skipped.Add($"صف {r}: الكود '{sku}' موجود مسبقاً — تم تخطيه");
                    continue;
                }

                if (!productsDict.TryGetValue(sku, out var product))
                {
                    var nameAr = GetVal(colNameAr);
                    var priceStr = GetVal(colPrice);

                    if (string.IsNullOrEmpty(nameAr) || string.IsNullOrEmpty(priceStr))
                    {
                        result.Errors.Add($"صف {r}: بيانات أساسية ناقصة (الاسم أو السعر) للكود {sku}");
                        continue;
                    }
                    if (!decimal.TryParse(priceStr, out var price))
                    {
                        result.Errors.Add($"صف {r}: السعر غير صحيح '{priceStr}' للكود {sku}");
                        continue;
                    }

                    // Resolve category
                    var mCat = GetVal(colMainCat);
                    var sCat = GetVal(colSubCat);
                    int? catId = categories.FirstOrDefault(c => (mCat != "" && c.NameAr == mCat) || (sCat != "" && c.NameAr == sCat))?.Id;

                    // Resolve Brand
                    var bStr = GetVal(colBrand);
                    int? bId = brands.FirstOrDefault(b => b.NameAr == bStr || b.NameEn == bStr || b.Id.ToString() == bStr)?.Id;

                    // Resolve Unit
                    var uStr = GetVal(colUnit);
                    int? uId = units.FirstOrDefault(u => u.NameAr == uStr)?.Id;

                    decimal? cost = decimal.TryParse(GetVal(colCost), out var cs) ? cs : null;
                    decimal? dP   = decimal.TryParse(GetVal(colDisc), out var dp) ? dp : null;

                    product = new Product
                    {
                        NameAr = nameAr,
                        NameEn = GetVal(colNameEn).NullIfEmpty() ?? nameAr,
                        SKU = sku,
                        Price = price,
                        DiscountPrice = dP,
                        CostPrice = cost,
                        CategoryId = catId ?? categories.FirstOrDefault()?.Id ?? 1,
                        BrandId = bId,
                        // UnitId = uId, // Reverted until Migration
                        DescriptionAr = GetVal(colDescAr).NullIfEmpty(),
                        DescriptionEn = GetVal(colDescEn).NullIfEmpty(),
                        IsFeatured    = GetVal(colFeat).Contains("نعم"),
                        Status = ProductStatus.Active,
                        CreatedAt = TimeHelper.GetEgyptTime()
                    };
                    productsDict[sku] = product;
                    _db.Products.Add(product);
                    result.Added++;
                }

                // Add Variant
                var stockVal = GetVal(colStock);
                var size     = GetVal(colSize).NullIfEmpty();
                var cEn      = GetVal(colColorEn).NullIfEmpty();
                var cAr      = GetVal(colColorAr).NullIfEmpty();

                if (!string.IsNullOrEmpty(stockVal))
                {
                    if (int.TryParse(stockVal, out var stk))
                    {
                        decimal? vAdj = decimal.TryParse(GetVal(colAdj), out var adj) ? adj : null;
                        
                        product.Variants.Add(new ProductVariant {
                            Size = size, Color = cEn, ColorAr = cAr,
                            StockQuantity = stk, PriceAdjustment = vAdj,
                            CreatedAt = TimeHelper.GetEgyptTime()
                        });
                        result.VariantsAdded++;
                    }
                    else result.Errors.Add($"صف {r}: كمية المخزون غير صحيحة");
                }
            }

            foreach(var p in productsDict.Values)
                if (p.Variants.Any()) p.TotalStock = p.Variants.Sum(v => v.StockQuantity);

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"خطأ: {ex.Message}" });
        }

        return Ok(new { message = "تم الاستيراد بنجاح", added = result.Added, variantsAdded = result.VariantsAdded, skipped = result.Skipped.Count, errors = result.Errors.Count, details = result });
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
            if (!p.Variants.Any())
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
                foreach (var v in p.Variants)
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
                .Include(p => p.Variants)
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

                var activeVariants = product.Variants.ToList();

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
