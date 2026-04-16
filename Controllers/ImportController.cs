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

        // 2. Write Category Hierarchy Ranges (starting col 10)
        int subCol = 10;
        foreach (var mCat in mainCats)
        {
            var subs = allCats.Where(c => c.ParentId == mCat.Id).ToList();
            if (subs.Any())
            {
                // Level 1 -> Level 2
                var subNames = subs.Select(s => s.NameAr).ToList();
                for (int i = 0; i < subNames.Count; i++) wsL.Cell(i + 1, subCol).Value = subNames[i];
                var range = wsL.Range(1, subCol, subNames.Count, subCol);
                wb.DefinedNames.Add(SafeName(mCat.NameAr), range);
                subCol++;

                // Level 2 -> Level 3
                foreach (var sub in subs)
                {
                    var subSubs = allCats.Where(c => c.ParentId == sub.Id).Select(c => c.NameAr).ToList();
                    if (subSubs.Any())
                    {
                        for (int i = 0; i < subSubs.Count; i++) wsL.Cell(i + 1, subCol).Value = subSubs[i];
                        var ssRange = wsL.Range(1, subCol, subSubs.Count, subCol);
                        wb.DefinedNames.Add(SafeName(sub.NameAr), ssRange);
                        subCol++;
                    }
                }
            }
        }

        // 3. Extra Lists
        void FillCol(int col, List<string> items) {
            for (int i = 0; i < items.Count; i++) wsL.Cell(i + 1, col).Value = items[i];
        }
        FillCol(3, brands);     
        FillCol(4, units);      
        FillCol(8, new List<string> { "نعم", "لا" });
        FillCol(9, new List<string> { "نشط", "مسودة", "مخفي" });

        var brandRange = wsL.Range(1, 3, Math.Max(1, brands.Count), 3);
        var unitRange  = wsL.Range(1, 4, Math.Max(1, units.Count), 4);
        var featRange  = wsL.Range(1, 8, 2, 8);
        var statRange  = wsL.Range(1, 9, 3, 9);

        var ws1 = wb.Worksheets.Add("المنتجات والمقاسات");
        ws1.RightToLeft = true;

        var headers1 = new[] {
            "الكود SKU *", "الاسم عربي *", "الوحدة", "الاسم انجليزي",
            "التصنيف الأساسي *", "التصنيف الفرعي", "التصنيف الفرعي 2",
            "سعر التكلفة", "السعر *", "سعر الخصم",
            "الماركة", "المقاس", "اللون (English)", "اللون (عربي)",
            "المخزون *", "فارق السعر للمقاس", "حد الطلب", "الحالة",
            "مميز (نعم/لا)", "الوصف عربي", "الوصف انجليزي"
        };
        for (int c = 0; c < headers1.Length; c++)
        {
            var cell = ws1.Cell(1, c+1);
            cell.Value = headers1[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
            cell.Style.Font.FontColor = XLColor.White;
        }

        ws1.Column(1).Style.NumberFormat.Format = "@"; 

        for (int r = 2; r <= 1000; r++)
        {
            // Main Category (Col 5)
            ws1.Cell(r, 5).CreateDataValidation().List(mCatRange, true);
            // Sub Category (Col 6) dependent on Main (Col 5)
            ws1.Cell(r, 6).CreateDataValidation().List("=INDIRECT(SUBSTITUTE(SUBSTITUTE(SUBSTITUTE(SUBSTITUTE($E" + r + ",\" \",\"_\"),\"-\",\"_\"),\"&\",\"_\"),\"/\",\"_\"))", true);
            // Sub-Sub Category (Col 7) dependent on Sub (Col 6)
            ws1.Cell(r, 7).CreateDataValidation().List("=INDIRECT(SUBSTITUTE(SUBSTITUTE(SUBSTITUTE(SUBSTITUTE($F" + r + ",\" \",\"_\"),\"-\",\"_\"),\"&\",\"_\"),\"/\",\"_\"))", true);
            
            ws1.Cell(r, 11).CreateDataValidation().List(brandRange, true);
            ws1.Cell(r, 3).CreateDataValidation().List(unitRange, true); // Unit is now Col 3
            
            ws1.Cell(r, 18).CreateDataValidation().List(statRange, true);
            ws1.Cell(r, 19).CreateDataValidation().List(featRange, true);
        }

        // Sample row 1
        ws1.Cell(2,1).Value = "TS-001";
        ws1.Cell(2,2).Value = "تيشرت رياضي";
        ws1.Cell(2,3).Value = units.FirstOrDefault() ?? "قطعة";
        ws1.Cell(2,4).Value = "Sports T-Shirt";
        ws1.Cell(2,5).Value = mainCats.FirstOrDefault()?.NameAr ?? "ملابس";
        ws1.Cell(2,8).Value = 200; 
        ws1.Cell(2,9).Value = 299;
        ws1.Cell(2,11).Value = brands.FirstOrDefault() ?? "Nike";
        ws1.Cell(2,12).Value = "M";
        ws1.Cell(2,13).Value = "Blue";
        ws1.Cell(2,14).Value = "أزرق";
        ws1.Cell(2,15).Value = 10;
        ws1.Cell(2,16).Value = 0;
        ws1.Cell(2,17).Value = 5; 
        ws1.Cell(2,18).Value = "نشط";
        ws1.Cell(2,19).Value = "لا";
        ws1.Row(2).Style.Font.FontColor = XLColor.Gray;

        ws1.Columns().AdjustToContents();

        var stream = new MemoryStream();
        wb.SaveAs(stream); stream.Position = 0;

        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "products_import_template_v3.xlsx");
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

            var ws = wb.Worksheets.FirstOrDefault(x => x.Visibility == XLWorksheetVisibility.Visible)
                     ?? wb.Worksheets.FirstOrDefault();

            if (ws == null) throw new Exception("الملف لا يحتوي على صفحات عمل");

            var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var firstRow = ws.Row(1);
            
            string Normalize(string s) => new string(s.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToLower();

            for (int c = 1; c <= (ws.LastColumnUsed()?.ColumnNumber() ?? 25); c++)
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

            // Mapping columns
            int colSku      = GetCol("الكود SKU", "الباركود", "sku");
            int colNameAr   = GetCol("الاسم عربي", "اسم المنتج", "الاسم");
            int colUnit     = GetCol("الوحدة", "وحدة القياس");
            int colNameEn   = GetCol("الاسم انجليزي", "الاسم English");
            int colMainCat  = GetCol("التصنيف الأساسي", "الفئة", "التصنيف");
            int colSubCat   = GetCol("التصنيف الفرعي", "الفئة الفرعية");
            int colSubSub   = GetCol("التصنيف الفرعي 2", "التصنيف فرع فرعي", "الفئة الفرعية 2");
            int colCost     = GetCol("سعر التكلفة", "التكلفة", "Cost");
            int colPrice    = GetCol("السعر", "سعر البيع");
            int colDisc     = GetCol("سعر الخصم", "الخصم");
            int colBrand    = GetCol("العلامة التجارية", "الماركة", "Brand");
            int colSize     = GetCol("المقاس");
            int colColorEn  = GetCol("اللون (English)", "اللون English");
            int colColorAr  = GetCol("اللون (عربي)", "اللون عربي");
            int colStock    = GetCol("المخزون");
            int colAdj      = GetCol("فارق السعر للمقاس");
            int colReorder  = GetCol("حد الطلب");
            int colStatus   = GetCol("الحالة");
            int colFeat     = GetCol("مميز (نعم/لا)");
            int colDescAr   = GetCol("الوصف عربي");
            int colDescEn   = GetCol("الوصف انجليزي");

            if (colSku == -1 || (colNameAr == -1 && colPrice == -1))
                return BadRequest(new { message = "تعذر التعرف على أعمدة الملف الأساسية (الكود، الاسم، السعر)" });

            var categories   = await _db.Categories.ToListAsync();
            var brands       = await _db.Brands.ToListAsync();
            var units        = await _db.ProductUnits.ToListAsync();
            var existingSkus = await _db.Products.Select(p => p.SKU).ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

            var productsDict = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            // Prepare Error Workbook for rejected rows
            using var errorWb = new XLWorkbook();
            var errorWs = errorWb.Worksheets.Add("الأسطر المرفوضة");
            errorWs.RightToLeft = true;
            
            // Copy headers to error worksheet
            for (int c = 1; c <= ws.LastColumnUsed().ColumnNumber(); c++)
            {
                var originalCell = firstRow.Cell(c);
                var errorHeaderCell = errorWs.Cell(1, c);
                errorHeaderCell.Value = originalCell.Value;
                errorHeaderCell.Style.Font.Bold = true;
                errorHeaderCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
                errorHeaderCell.Style.Font.FontColor = XLColor.White;
            }
            int colErrDesc = ws.LastColumnUsed().ColumnNumber() + 1;
            errorWs.Cell(1, colErrDesc).Value = "سبب الرفض";
            errorWs.Cell(1, colErrDesc).Style.Font.Bold = true;
            errorWs.Cell(1, colErrDesc).Style.Fill.BackgroundColor = XLColor.Red;
            errorWs.Cell(1, colErrDesc).Style.Font.FontColor = XLColor.White;

            int errorRowIdx = 2;

            void LogRowError(int r, string message)
            {
                result.Errors.Add($"صف {r}: {message}");
                // Copy the entire row from the original sheet to the error sheet
                for (int c = 1; c <= ws.LastColumnUsed().ColumnNumber(); c++)
                {
                    errorWs.Cell(errorRowIdx, c).Value = ws.Cell(r, c).Value;
                }
                errorWs.Cell(errorRowIdx, colErrDesc).Value = message;
                errorWs.Cell(errorRowIdx, colErrDesc).Style.Font.FontColor = XLColor.Red;
                errorRowIdx++;
            }

            for (int r = 2; r <= lastRow; r++)
            {
                string GetVal(int col) => col != -1 ? ws.Cell(r, col).GetString().Trim() : "";

                var sku = GetVal(colSku);
                if (string.IsNullOrEmpty(sku)) continue;

                var isExisting = existingSkus.Contains(sku);

                if (isExisting && !update)
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
                        LogRowError(r, $"بيانات أساسية ناقصة (الاسم '{nameAr}' أو السعر '{priceStr}') للكود {sku}");
                        continue;
                    }
                    if (!decimal.TryParse(priceStr, out var price))
                    {
                        LogRowError(r, $"السعر غير صحيح '{priceStr}' للكود {sku}");
                        continue;
                    }

                    // Resolve category chain (Main -> Sub -> SubSub)
                    var mainC = GetVal(colMainCat);
                    var subC  = GetVal(colSubCat);
                    var subS  = GetVal(colSubSub);
                    
                    int? catId = null;
                    if (!string.IsNullOrEmpty(mainC))
                    {
                        var parent = categories.FirstOrDefault(c => c.NameAr == mainC && c.ParentId == null);
                        if (parent != null)
                        {
                            catId = parent.Id;
                            if (!string.IsNullOrEmpty(subC))
                            {
                                var sub = categories.FirstOrDefault(c => c.NameAr == subC && c.ParentId == parent.Id);
                                if (sub != null)
                                {
                                    catId = sub.Id;
                                    if (!string.IsNullOrEmpty(subS))
                                    {
                                        var leaf = categories.FirstOrDefault(c => c.NameAr == subS && c.ParentId == sub.Id);
                                        if (leaf != null) catId = leaf.Id;
                                    }
                                }
                            }
                        }
                    }

                    // Resolve Brand
                    var bStr = GetVal(colBrand);
                    int? bId = brands.FirstOrDefault(b => b.NameAr == bStr || b.Id.ToString() == bStr)?.Id;

                    // Resolve Unit
                    var uStr = GetVal(colUnit);
                    int? uId = units.FirstOrDefault(u => u.NameAr == uStr)?.Id;

                    decimal? cost = decimal.TryParse(GetVal(colCost), out var cs) ? cs : null;
                    decimal? dP   = decimal.TryParse(GetVal(colDisc), out var dp) ? dp : null;
                    int rL = int.TryParse(GetVal(colReorder), out var rl) ? rl : 0;
                    
                    var status = GetVal(colStatus) switch {
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

                // Add/Update Variant
                var stockVal = GetVal(colStock);
                var size     = GetVal(colSize).NullIfEmpty();
                var cEn      = GetVal(colColorEn).NullIfEmpty();
                var cAr      = GetVal(colColorAr).NullIfEmpty();

                if (!string.IsNullOrEmpty(stockVal) && int.TryParse(stockVal, out var stk))
                {
                    decimal? vAdj = decimal.TryParse(GetVal(colAdj), out var adj) ? adj : null;
                    if (product != null)
                    {
                        var variant = (isExisting && update) 
                            ? product.Variants.FirstOrDefault(v => (v.Size ?? "") == (size ?? "") && (v.ColorAr ?? "") == (cAr ?? ""))
                            : null;

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
                else if (!string.IsNullOrEmpty(stockVal))
                {
                    LogRowError(r, $"كمية المخزون غير صحيحة '{stockVal}'");
                }
            }

            foreach(var p in productsDict.Values)
                if (p.Variants.Any()) p.TotalStock = p.Variants.Sum(v => v.StockQuantity);

            await _db.SaveChangesAsync();

            // If there were errors, return the error report file
            if (result.Errors.Any())
            {
                errorWs.Columns().AdjustToContents();
                var errStream = new MemoryStream();
                errorWb.SaveAs(errStream);
                errStream.Position = 0;
                return File(errStream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"import_errors_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
            }
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
