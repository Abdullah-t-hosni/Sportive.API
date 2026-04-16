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
        var allCats      = await _db.Categories.ToListAsync();
        var mainCats     = allCats.Where(c => c.ParentId == null).ToList();
        var brands       = await _db.Brands.Select(b => b.NameAr).ToListAsync();
        var units        = await _db.ProductUnits.Select(u => u.NameAr).ToListAsync();
        
        var sizes    = await _db.ProductVariants.Where(v => !string.IsNullOrEmpty(v.Size)).Select(v => v.Size!).Distinct().ToListAsync();
        var colorEnList = await _db.ProductVariants.Where(v => !string.IsNullOrEmpty(v.Color)).Select(v => v.Color!).Distinct().ToListAsync();
        var colorArList = await _db.ProductVariants.Where(v => !string.IsNullOrEmpty(v.ColorAr)).Select(v => v.ColorAr!).Distinct().ToListAsync();

        using var wb = new XLWorkbook();
        var wsL = wb.Worksheets.Add("Lists");
        wsL.Hide();
        
        string SafeName(string s) => s.Replace(" ", "_").Replace("-", "_").Replace("&", "_").Replace("/", "_");

        // 1. Write Main Categories to Col 1
        for (int i = 0; i < mainCats.Count; i++) wsL.Cell(i + 1, 1).Value = mainCats[i].NameAr;
        var mCatRange = wsL.Range(1, 1, Math.Max(1, mainCats.Count), 1);
        wb.DefinedNames.Add("MainCategoriesList", mCatRange);

        // 2. Write Sub Categories for each Main Cat in separate columns (starting col 10)
        int subCol = 10;
        foreach (var mCat in mainCats)
        {
            var subs = allCats.Where(c => c.ParentId == mCat.Id).Select(c => c.NameAr).ToList();
            if (subs.Any())
            {
                for (int i = 0; i < subs.Count; i++) wsL.Cell(i + 1, subCol).Value = subs[i];
                var range = wsL.Range(1, subCol, subs.Count, subCol);
                wb.DefinedNames.Add(SafeName(mCat.NameAr), range);
                subCol++;
            }
        }

        // 3. Extra Lists
        void FillCol(int col, List<string> items) {
            for (int i = 0; i < items.Count; i++) wsL.Cell(i + 1, col).Value = items[i];
        }
        FillCol(3, brands);     
        FillCol(4, units);      
        FillCol(5, sizes);      
        FillCol(6, colorEnList);   
        FillCol(7, colorArList);   
        FillCol(8, new List<string> { "نعم", "لا" });
        FillCol(9, new List<string> { "نشط", "مسودة", "مخفي" });

        var brandRange = wsL.Range(1, 3, Math.Max(1, brands.Count), 3);
        var unitRange  = wsL.Range(1, 4, Math.Max(1, units.Count), 4);
        var sizeRange  = wsL.Range(1, 5, Math.Max(1, sizes.Count), 5);
        var cEnRange   = wsL.Range(1, 6, Math.Max(1, colorEnList.Count), 6);
        var cArRange   = wsL.Range(1, 7, Math.Max(1, colorArList.Count), 7);
        var featRange  = wsL.Range(1, 8, 2, 8);
        var statRange  = wsL.Range(1, 9, 3, 9);

        var ws1 = wb.Worksheets.Add("المنتجات والمقاسات");
        ws1.RightToLeft = true;

        var headers1 = new[] {
            "الاسم عربي *","الاسم انجليزي","التصنيف الأساسي *","التصنيف الفرعي","الكود SKU *",
            "السعر *","سعر الخصم","سعر التكلفة","الماركة","الوحدة",
            "المقاس","اللون (English)","اللون (عربي)","المخزون *","فارق السعر للمقاس",
            "حد الطلب","الحالة","مميز (نعم/لا)","الوصف عربي","الوصف انجليزي"
        };
        for (int c = 0; c < headers1.Length; c++)
        {
            var cell = ws1.Cell(1, c+1);
            cell.Value = headers1[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
            cell.Style.Font.FontColor = XLColor.White;
        }

        ws1.Column(5).Style.NumberFormat.Format = "@"; 

        for (int r = 2; r <= 1000; r++)
        {
            ws1.Cell(r, 3).CreateDataValidation().List(mCatRange, true);
            // Dependent list for Sub-Category (Col 4) based on Main-Category (Col 3)
            ws1.Cell(r, 4).CreateDataValidation().List("=INDIRECT(SUBSTITUTE(SUBSTITUTE(SUBSTITUTE(SUBSTITUTE($C" + r + ",\" \",\"_\"),\"-\",\"_\"),\"&\",\"_\"),\"/\",\"_\"))", true);
            
            ws1.Cell(r, 9).CreateDataValidation().List(brandRange, true);
            ws1.Cell(r, 10).CreateDataValidation().List(unitRange, true);
            ws1.Cell(r, 11).CreateDataValidation().List(sizeRange, true);
            ws1.Cell(r, 12).CreateDataValidation().List(cEnRange, true);
            ws1.Cell(r, 13).CreateDataValidation().List(cArRange, true);
            ws1.Cell(r, 17).CreateDataValidation().List(statRange, true);
            ws1.Cell(r, 18).CreateDataValidation().List(featRange, true);
        }

        // Sample row 1 (Variant 1)
        ws1.Cell(2,1).Value = "تيشرت رياضي";
        ws1.Cell(2,2).Value = "Sports T-Shirt";
        ws1.Cell(2,3).Value = mainCats.FirstOrDefault()?.NameAr ?? "ملابس";
        ws1.Cell(2,5).Value = "TS-001";
        ws1.Cell(2,6).Value = 299;
        ws1.Cell(2,8).Value = 200; 
        ws1.Cell(2,9).Value = brands.FirstOrDefault() ?? "Nike";
        ws1.Cell(2,10).Value = units.FirstOrDefault() ?? "قطعة";
        ws1.Cell(2,11).Value = "M";
        ws1.Cell(2,12).Value = "Blue";
        ws1.Cell(2,13).Value = "أزرق";
        ws1.Cell(2,14).Value = 10;
        ws1.Cell(2,15).Value = 0;
        ws1.Cell(2,16).Value = 5; 
        ws1.Cell(2,17).Value = "نشط";
        ws1.Cell(2,18).Value = "لا";
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
    public async Task<IActionResult> ImportProducts(IFormFile file, [FromQuery] bool update = false)
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

            // Find the correct worksheet (Skip hidden "Lists" sheet)
            var ws = wb.Worksheets.FirstOrDefault(x => x.Visibility == XLWorksheetVisibility.Visible)
                     ?? wb.Worksheets.FirstOrDefault();

            if (ws == null) throw new Exception("الملف لا يحتوي على صفحات عمل");

            var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var firstRow = ws.Row(1);
            
            // Normalize header text for comparison (remove spaces, symbols, and asterisks)
            string Normalize(string s) => new string(s.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToLower();

            for (int c = 1; c <= (ws.LastColumnUsed()?.ColumnNumber() ?? 20); c++)
            {
                var hRaw = firstRow.Cell(c).GetString().Trim();
                if (string.IsNullOrEmpty(hRaw)) continue;
                
                var hNorm = Normalize(hRaw);
                if (!headers.ContainsKey(hNorm))
                    headers[hNorm] = c;
            }

            int GetCol(params string[] aliases) {
                foreach (var a in aliases) {
                    var aNorm = Normalize(a);
                    if (headers.TryGetValue(aNorm, out var idx)) return idx;
                }
                return -1;
            }

            // Mapping columns with normalized aliases
            int colNameAr   = GetCol("الاسم عربي", "اسم المنتج", "الاسم");
            int colNameEn   = GetCol("الاسم انجليزي", "الاسم English");
            int colMainCat  = GetCol("التصنيف الأساسي", "الفئة", "التصنيف", "كود الفئة");
            int colSubCat   = GetCol("التصنيف الفرعي", "الفئة الفرعية");
            int colSku      = GetCol("الكود SKU", "الباركود", "sku");
            int colPrice    = GetCol("السعر", "سعر البيع");
            int colDisc     = GetCol("سعر الخصم", "الخصم", "Discount");
            int colCost     = GetCol("سعر التكلفة", "التكلفة", "Cost");
            int colBrand    = GetCol("العلامة التجارية", "الماركة", "Brand");
            int colUnit     = GetCol("الوحدة", "وحدة القياس");
            int colSize     = GetCol("المقاس");
            int colColorEn  = GetCol("اللون (English)", "اللون English");
            int colColorAr  = GetCol("اللون (عربي)", "اللون عربي");
            int colStock    = GetCol("المخزون");
            int colAdj      = GetCol("فارق السعر", "فارق السعر للمقاس");
            int colReorder  = GetCol("حد الطلب", "الحد الأدنى للمخزون");
            int colStatus   = GetCol("الحالة", "الوضع");
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

                var isExisting = existingSkus.Contains(sku);

                if (isExisting && !update)
                {
                    if (!result.Skipped.Any(s => s.Contains($"الكود '{sku}' موجود")))
                        result.Skipped.Add($"صف {r}: الكود '{sku}' موجود مسبقاً — تم تخطيه (فعل خيار التحديث للتعديل)");
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
                    int rL = int.TryParse(GetVal(colReorder), out var rl) ? rl : 0;
                    
                    var statusStr = GetVal(colStatus);
                    var status = statusStr switch {
                        "مسودة" => ProductStatus.Draft,
                        "مخفي" => ProductStatus.Hidden,
                        _ => ProductStatus.Active
                    };

                    if (isExisting && update)
                    {
                        product = await _db.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.SKU == sku);
                        if (product != null)
                        {
                            product.NameAr = nameAr;
                            product.NameEn = GetVal(colNameEn).NullIfEmpty() ?? nameAr;
                            product.Price = price;
                            product.DiscountPrice = dP;
                            product.CostPrice = cost;
                            product.CategoryId = catId ?? product.CategoryId;
                            product.BrandId = bId ?? product.BrandId;
                            product.ReorderLevel = rL;
                            product.Status = status;
                            product.IsFeatured = GetVal(colFeat).Contains("نعم");
                            product.DescriptionAr = GetVal(colDescAr).NullIfEmpty() ?? product.DescriptionAr;
                            product.DescriptionEn = GetVal(colDescEn).NullIfEmpty() ?? product.DescriptionEn;
                            
                            productsDict[sku] = product;
                            result.Updated++;
                        }
                    }
                    else
                    {
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
                            ReorderLevel = rL,
                            DescriptionAr = GetVal(colDescAr).NullIfEmpty(),
                            DescriptionEn = GetVal(colDescEn).NullIfEmpty(),
                            IsFeatured    = GetVal(colFeat).Contains("نعم"),
                            Status = status,
                            CreatedAt = TimeHelper.GetEgyptTime()
                        };
                        productsDict[sku] = product;
                        _db.Products.Add(product);
                        result.Added++;
                    }
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
                        
                        if (product != null)
                        {
                            // If updating, try to find matching variant first
                            ProductVariant? variant = null;
                            if (isExisting && update)
                            {
                                variant = product.Variants.FirstOrDefault(v => 
                                    (v.Size ?? "").Equals(size ?? "", StringComparison.OrdinalIgnoreCase) && 
                                    (v.ColorAr ?? "").Equals(cAr ?? "", StringComparison.OrdinalIgnoreCase));
                            }

                            if (variant != null)
                            {
                                variant.StockQuantity = stk;
                                variant.PriceAdjustment = vAdj;
                            }
                            else
                            {
                                product.Variants.Add(new ProductVariant {
                                    Size = size, Color = cEn, ColorAr = cAr,
                                    StockQuantity = stk, PriceAdjustment = vAdj,
                                    CreatedAt = TimeHelper.GetEgyptTime()
                                });
                                result.VariantsAdded++;
                            }
                        }
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

        return Ok(new { message = "تم الاستيراد بنجاح", added = result.Added, updated = result.Updated, variantsAdded = result.VariantsAdded, skipped = result.Skipped.Count, errors = result.Errors.Count, details = result });
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
        public int Updated       { get; set; }
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
