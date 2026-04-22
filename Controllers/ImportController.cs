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
        var allCats      = await _db.Categories.Where(c => c.NameAr != null).ToListAsync();
        var mainCats     = allCats.Where(c => c.ParentId == null).ToList();
        var brands       = await _db.Brands.Where(b => b.NameAr != null).Select(b => b.NameAr!).ToListAsync();
        var units        = await _db.ProductUnits.Where(u => u.NameAr != null).Select(u => u.NameAr!).ToListAsync();
        
        // Fetch existing sizes and colors to provide as options
        var existingSizes  = await _db.ProductVariants.Where(v => v.Size != null).Select(v => v.Size!).Distinct().ToListAsync();
        var catNames = allCats.Select(c => c.NameAr).Where(n => n != null).ToHashSet();
        var existingColors = await _db.ProductVariants
            .Where(v => v.ColorAr != null)
            .Select(v => v.ColorAr!)
            .Distinct()
            .ToListAsync();
        
        // Filter out colors that are actually category names or too long
        existingColors = existingColors
            .Where(c => !catNames.Contains(c) && c.Length < 25)
            .ToList();

        var standardColors = new List<string> { 
            "أبيض", "أسود", "أحمر", "أزرق", "أخضر", "أصفر", "رمادي", "كحلي", "بني", "بيج", "برتقالي", "بنفسجي", "سماوي", "ذهبي", "فضي" 
        };
        existingColors = existingColors.Concat(standardColors).Distinct().ToList();

        using var wb = new XLWorkbook();
        var wsL = wb.Worksheets.Add("Lists");
        wsL.Hide();

        // 1. Write Main Categories to Col 1
        for (int i = 0; i < mainCats.Count; i++) wsL.Cell(i + 1, 1).Value = mainCats[i].NameAr;
        var mCatRange = wsL.Range(1, 1, Math.Max(1, mainCats.Count), 1);
        wb.DefinedNames.Add("MainCategoriesList", mCatRange);

        // 2. Extra Lists (Fixed Columns 2-10)
        void FillCol(int col, List<string> items) {
            for (int i = 0; i < items.Count; i++) wsL.Cell(i + 1, col).Value = items[i];
        }

        if (!existingSizes.Any()) existingSizes = new List<string> { "S", "M", "L", "XL", "XXL", "3XL", "Free Size" };
        if (!existingColors.Any()) existingColors = new List<string> { "أبيض", "أسود", "أحمر", "أزرق", "أخضر", "رمادي", "كحلي", "بني" };

        FillCol(2, brands);     
        FillCol(3, units);      
        FillCol(4, new List<string> { "نعم", "لا" });
        FillCol(5, new List<string> { "نشط", "مسودة", "مخفي" });
        FillCol(6, existingSizes);
        FillCol(7, existingColors);

        var brandRange = wsL.Range(1, 2, Math.Max(1, brands.Count), 2);
        var unitRange  = wsL.Range(1, 3, Math.Max(1, units.Count), 3);
        var yesNoRange = wsL.Range(1, 4, 2, 4);
        var statRange  = wsL.Range(1, 5, 3, 5);
        var sizeRange  = wsL.Range(1, 6, Math.Max(1, existingSizes.Count), 6);
        var colorRange = wsL.Range(1, 7, Math.Max(1, existingColors.Count), 7);

        // 3. Category Mapping (Cols 10-11) - Robust OFFSET/MATCH approach
        var mapping = new List<(string Parent, string Child)>();
        foreach (var mCat in mainCats)
        {
            var subs = allCats.Where(c => c.ParentId == mCat.Id).ToList();
            foreach (var sub in subs)
            {
                mapping.Add((mCat.NameAr!, sub.NameAr!));
                var subSubs = allCats.Where(c => c.ParentId == sub.Id).ToList();
                foreach (var ss in subSubs)
                {
                    mapping.Add((sub.NameAr!, ss.NameAr!));
                }
            }
        }
        
        // Group by parent to ensure children are contiguous for OFFSET/MATCH
        mapping = mapping.OrderBy(m => m.Parent).ToList();
        
        for (int i = 0; i < mapping.Count; i++)
        {
            wsL.Cell(i + 1, 10).Value = mapping[i].Parent;
            wsL.Cell(i + 1, 11).Value = mapping[i].Child;
        }
        
        // Define Names for mapping columns to keep formulas clean
        var parentRange = wsL.Range(1, 10, Math.Max(1, mapping.Count), 10);
        var childRange  = wsL.Range(1, 11, Math.Max(1, mapping.Count), 11);
        wb.DefinedNames.Add("MapParent", parentRange);
        wb.DefinedNames.Add("MapChild", childRange);

        var ws1 = wb.Worksheets.Add("المنتجات والمقاسات");
        ws1.RightToLeft = true;

        var headers1 = new[] {
            "الكود SKU *", "الاسم عربي *", "الوحدة *", "الاسم انجليزي",
            "التصنيف الأساسي *", "التصنيف الفرعي", "التصنيف الفرعي 2",
            "سعر التكلفة", "السعر *", "سعر الخصم",
            "خاضع للضريبة؟ *", "الماركة", "المقاس", "اللون (English)", "اللون (عربي)",
            "المخزون", "فارق السعر للمقاس", "حد الطلب", "الحالة",
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

        for (int r = 2; r <= 500; r++)
        {
            ws1.Cell(r, 5).CreateDataValidation().List(mCatRange, true);
            
            // SubCategory (Col 6) based on MainCategory (Col 5)
            // =OFFSET(Lists!$K$1, MATCH($E2, Lists!$J:$J, 0)-1, 0, COUNTIF(Lists!$J:$J, $E2), 1)
            ws1.Cell(r, 6).CreateDataValidation().List("=OFFSET(Lists!$K$1, MATCH($E" + r + ", Lists!$J:$J, 0)-1, 0, COUNTIF(Lists!$J:$J, $E" + r + "), 1)", true);
            
            // SubSubCategory (Col 7) based on SubCategory (Col 6)
            ws1.Cell(r, 7).CreateDataValidation().List("=OFFSET(Lists!$K$1, MATCH($F" + r + ", Lists!$J:$J, 0)-1, 0, COUNTIF(Lists!$J:$J, $F" + r + "), 1)", true);
            
            ws1.Cell(r, 12).CreateDataValidation().List(brandRange, true);
            ws1.Cell(r, 3).CreateDataValidation().List(unitRange, true);
            ws1.Cell(r, 11).CreateDataValidation().List(yesNoRange, true);
            ws1.Cell(r, 19).CreateDataValidation().List(statRange, true);
            ws1.Cell(r, 20).CreateDataValidation().List(yesNoRange, true);

            ws1.Cell(r, 13).CreateDataValidation().List(sizeRange, true);
            ws1.Cell(r, 15).CreateDataValidation().List(colorRange, true);
        }

        ws1.Cell(2,1).Value = "TS-001";
        ws1.Cell(2,2).Value = "تيشرت رياضي";
        ws1.Cell(2,3).Value = units.FirstOrDefault() ?? "قطعة";
        ws1.Cell(2,5).Value = mainCats.FirstOrDefault()?.NameAr;
        ws1.Cell(2,8).Value = 200; 
        ws1.Cell(2,9).Value = 299;
        ws1.Cell(2,11).Value = "نعم";
        ws1.Cell(2,19).Value = "نشط";
        ws1.Cell(2,20).Value = "لا";
        ws1.Row(2).Style.Font.FontColor = XLColor.Gray;

        ws1.Columns().AdjustToContents();
        var stream = new MemoryStream();
        wb.SaveAs(stream); stream.Position = 0;

        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "products_import_template.xlsx");
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
            int colSku      = GetCol("الكود SKU", "الباركود", "sku", "Code");
            int colNameAr   = GetCol("الاسم عربي", "اسم المنتج", "الاسم", "Name Ar");
            int colUnit     = GetCol("الوحدة", "وحدة القياس", "Unit");
            int colNameEn   = GetCol("الاسم انجليزي", "الاسم English", "Name En");
            int colMainCat  = GetCol("التصنيف الأساسي", "الفئة", "التصنيف", "Main Category", "Category");
            int colSubCat   = GetCol("التصنيف الفرعي", "الفئة الفرعية", "Sub Category");
            int colSubSub   = GetCol("التصنيف الفرعي 2", "التصنيف فرع فرعي", "الفئة الفرعية 2", "Sub Sub Category");
            int colCost     = GetCol("سعر التكلفة", "التكلفة", "Cost");
            int colPrice    = GetCol("السعر", "سعر البيع", "Price");
            int colDisc     = GetCol("سعر الخصم", "الخصم", "Discount");
            int colHasTax   = GetCol("خاضع للضريبة", "taxable", "الضريبة", "Is Taxable"); 
            int colBrand    = GetCol("العلامة التجارية", "الماركة", "Brand", "الماركه");
            int colSize     = GetCol("المقاس", "Size", "القياس", "المقاسات", "size");
            int colColorEn  = GetCol("اللون (English)", "اللون English", "Color En", "Color");
            int colColorAr  = GetCol("اللون (عربي)", "اللون عربي", "Color Ar", "اللون", "الوان");
            int colStock    = GetCol("المخزون", "Stock", "الكمية");
            int colAdj      = GetCol("فارق السعر للمقاس", "Price Adjustment");
            int colReorder  = GetCol("حد الطلب", "Reorder Level");
            int colStatus   = GetCol("الحالة", "Status");
            int colFeat     = GetCol("مميز (نعم/لا)", "Featured");
            int colDescAr   = GetCol("الوصف عربي", "Description Ar");
            int colDescEn   = GetCol("الوصف انجليزي", "Description En");

            if (colSku == -1 || colNameAr == -1 || colPrice == -1 || colUnit == -1 || colMainCat == -1)
                return BadRequest(new { message = "الأعمدة الإلزامية ناقصة (الكود، الاسم، السعر، الوحدة، التصنيف الأساسي)" });

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
            var lastCol = ws.LastColumnUsed();
            if (lastCol != null)
            {
                for (int c = 1; c <= lastCol.ColumnNumber(); c++)
                {
                    var originalCell = firstRow.Cell(c);
                    var errorHeaderCell = errorWs.Cell(1, c);
                    errorHeaderCell.Value = originalCell.Value;
                    errorHeaderCell.Style.Font.Bold = true;
                    errorHeaderCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
                    errorHeaderCell.Style.Font.FontColor = XLColor.White;
                }
            }
            int colErrDesc = (lastCol?.ColumnNumber() ?? 20) + 1;
            errorWs.Cell(1, colErrDesc).Value = "سبب الرفض";
            errorWs.Cell(1, colErrDesc).Style.Font.Bold = true;
            errorWs.Cell(1, colErrDesc).Style.Fill.BackgroundColor = XLColor.Red;
            errorWs.Cell(1, colErrDesc).Style.Font.FontColor = XLColor.White;

            int errorRowIdx = 2;

            void LogRowError(int r, string message)
            {
                result.Errors.Add($"صف {r}: {message}");
                // Copy the entire row from the original sheet to the error sheet
                var lCol = ws.LastColumnUsed()?.ColumnNumber() ?? 20;
                for (int c = 1; c <= lCol; c++)
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
                    if (isExisting)
                    {
                        product = await _db.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.SKU == sku);
                        if (product != null)
                        {
                            productsDict[sku] = product;
                            if (update)
                            {
                                var nameAr   = GetVal(colNameAr);
                                var priceStr = GetVal(colPrice);
                                var unitStr  = GetVal(colUnit);
                                var mainC    = GetVal(colMainCat);

                                if (!string.IsNullOrEmpty(nameAr)) product.NameAr = nameAr;
                                product.NameEn = GetVal(colNameEn).NullIfEmpty() ?? product.NameEn;
                                if (decimal.TryParse(priceStr, out var price)) product.Price = price;
                                product.DiscountPrice = decimal.TryParse(GetVal(colDisc), out var dp) ? dp : product.DiscountPrice;
                                product.CostPrice = decimal.TryParse(GetVal(colCost), out var cs) ? cs : product.CostPrice;
                                
                                // Resolve category chain for existing product update
                                if (!string.IsNullOrEmpty(mainC))
                                {
                                    var parent = categories.FirstOrDefault(c => string.Equals((c.NameAr ?? "").Trim(), mainC.Trim(), StringComparison.OrdinalIgnoreCase) && c.ParentId == null);
                                    if (parent != null)
                                    {
                                        product.CategoryId = parent.Id;
                                        var subC = GetVal(colSubCat);
                                        if (!string.IsNullOrEmpty(subC))
                                        {
                                            var sub = categories.FirstOrDefault(c => string.Equals((c.NameAr ?? "").Trim(), subC.Trim(), StringComparison.OrdinalIgnoreCase) && c.ParentId == parent.Id);
                                            if (sub != null)
                                            {
                                                product.CategoryId = sub.Id;
                                                var subS = GetVal(colSubSub);
                                                if (!string.IsNullOrEmpty(subS))
                                                {
                                                    var leaf = categories.FirstOrDefault(c => string.Equals((c.NameAr ?? "").Trim(), subS.Trim(), StringComparison.OrdinalIgnoreCase) && c.ParentId == sub.Id);
                                                    if (leaf != null) product.CategoryId = leaf.Id;
                                                }
                                            }
                                        }
                                    }
                                }

                                var bStr = GetVal(colBrand);
                                if (!string.IsNullOrEmpty(bStr))
                                    product.BrandId = brands.FirstOrDefault(b => string.Equals((b.NameAr ?? "").Trim(), bStr.Trim(), StringComparison.OrdinalIgnoreCase) || b.Id.ToString() == bStr)?.Id ?? product.BrandId;

                                if (!string.IsNullOrEmpty(unitStr))
                                    product.UnitId = units.FirstOrDefault(u => string.Equals((u.NameAr ?? "").Trim(), unitStr.Trim(), StringComparison.OrdinalIgnoreCase))?.Id ?? product.UnitId;

                                product.HasTax = !GetVal(colHasTax).Contains("لا");
                                product.ReorderLevel = int.TryParse(GetVal(colReorder), out var rl) ? rl : product.ReorderLevel;
                                product.Status = GetVal(colStatus) switch { "مسودة" => ProductStatus.Draft, "مخفي" => ProductStatus.Hidden, _ => ProductStatus.Active };
                                product.IsFeatured = GetVal(colFeat).Contains("نعم");
                                product.DescriptionAr = GetVal(colDescAr).NullIfEmpty() ?? product.DescriptionAr;
                                product.DescriptionEn = GetVal(colDescEn).NullIfEmpty() ?? product.DescriptionEn;
                                
                                result.Updated++;
                            }
                        }
                    }
                    else
                    {
                        var nameAr   = GetVal(colNameAr);
                        var priceStr = GetVal(colPrice);
                        var unitStr  = GetVal(colUnit);
                        var mainC    = GetVal(colMainCat);

                        // Mandatory checks for new products
                        if (string.IsNullOrEmpty(nameAr) || string.IsNullOrEmpty(priceStr) || string.IsNullOrEmpty(unitStr) || string.IsNullOrEmpty(mainC))
                        {
                            LogRowError(r, $"بيانات إلزامية ناقصة (الاسم '{nameAr}', السعر '{priceStr}', الوحدة '{unitStr}', التصنيف '{mainC}') — يجب ملء كافة المعلومات الأساسية");
                            continue;
                        }

                        if (!decimal.TryParse(priceStr, out var price))
                        {
                            LogRowError(r, $"السعر غير صحيح '{priceStr}' للكود {sku}");
                            continue;
                        }

                        int? catId = null;
                        var parent = categories.FirstOrDefault(c => string.Equals((c.NameAr ?? "").Trim(), mainC.Trim(), StringComparison.OrdinalIgnoreCase) && c.ParentId == null);
                        if (parent != null)
                        {
                            catId = parent.Id;
                            var subC = GetVal(colSubCat);
                            if (!string.IsNullOrEmpty(subC))
                            {
                                var sub = categories.FirstOrDefault(c => string.Equals((c.NameAr ?? "").Trim(), subC.Trim(), StringComparison.OrdinalIgnoreCase) && c.ParentId == parent.Id);
                                if (sub != null)
                                {
                                    catId = sub.Id;
                                    var subS = GetVal(colSubSub);
                                    if (!string.IsNullOrEmpty(subS))
                                    {
                                        var leaf = categories.FirstOrDefault(c => string.Equals((c.NameAr ?? "").Trim(), subS.Trim(), StringComparison.OrdinalIgnoreCase) && c.ParentId == sub.Id);
                                        if (leaf != null) catId = leaf.Id;
                                    }
                                }
                            }
                        }
                        else
                        {
                            LogRowError(r, $"التصنيف الأساسي '{mainC}' غير موجود في النظام");
                            continue;
                        }

                        var bStr = GetVal(colBrand);
                        int? bId = brands.FirstOrDefault(b => string.Equals((b.NameAr ?? "").Trim(), bStr.Trim(), StringComparison.OrdinalIgnoreCase) || b.Id.ToString() == bStr)?.Id;

                        int? uId = units.FirstOrDefault(u => string.Equals((u.NameAr ?? "").Trim(), unitStr.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
                        if (uId == null)
                        {
                            LogRowError(r, $"وحدة القياس '{unitStr}' غير موجودة");
                            continue;
                        }

                        product = new Product
                        {
                            NameAr = nameAr,
                            NameEn = GetVal(colNameEn).NullIfEmpty() ?? nameAr,
                            SKU = sku, Price = price,
                            DiscountPrice = decimal.TryParse(GetVal(colDisc), out var dp) ? dp : null,
                            CostPrice = decimal.TryParse(GetVal(colCost), out var cs) ? cs : null,
                            CategoryId = catId.Value, BrandId = bId, UnitId = uId,
                            HasTax = !GetVal(colHasTax).Contains("لا"),
                            ReorderLevel = int.TryParse(GetVal(colReorder), out var rl) ? rl : 0,
                            DescriptionAr = GetVal(colDescAr).NullIfEmpty(),
                            DescriptionEn = GetVal(colDescEn).NullIfEmpty(),
                            IsFeatured    = GetVal(colFeat).Contains("نعم"),
                            Status = GetVal(colStatus) switch { "مسودة" => ProductStatus.Draft, "مخفي" => ProductStatus.Hidden, _ => ProductStatus.Active },
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
                var adjStr   = GetVal(colAdj);

                bool hasVariantInfo = !string.IsNullOrEmpty(stockVal) || !string.IsNullOrEmpty(size) || 
                                     !string.IsNullOrEmpty(cEn) || !string.IsNullOrEmpty(cAr) ||
                                     !string.IsNullOrEmpty(adjStr);

                if (hasVariantInfo)
                {
                    int stk = int.TryParse(stockVal, out var sVal) ? sVal : 0;
                    decimal? vAdj = decimal.TryParse(adjStr, out var adj) ? adj : null;

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
            }

            foreach(var p in productsDict.Values)
                if (p.Variants.Any()) p.TotalStock = p.Variants.Sum(v => v.StockQuantity);

            await _db.SaveChangesAsync();

            string? errorReportBase64 = null;
            if (result.Errors.Any())
            {
                errorWs.Columns().AdjustToContents();
                using var errStream = new MemoryStream();
                errorWb.SaveAs(errStream);
                errorReportBase64 = Convert.ToBase64String(errStream.ToArray());
            }

            return Ok(new { 
                message = result.Errors.Any() ? "تم الاستيراد مع وجود بعض الأخطاء" : "تم الاستيراد بنجاح", 
                added = result.Added, 
                updated = result.Updated, 
                variantsAdded = result.VariantsAdded, 
                skipped = result.Skipped.Count, 
                errors = result.Errors.Count, 
                details = result,
                errorReportFile = errorReportBase64
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"خطأ: {ex.Message}" });
        }
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

    // ── ACCOUNTS TEMPLATE ─────────────────────────────────────
    [HttpGet("accounts-template")]
    public IActionResult GetAccountsTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("شجرة الحسابات");
        ws.RightToLeft = true;

        var headers = new[] { "كود الحساب *", "الاسم عربي *", "الاسم انجليزي", "النوع (Asset/Liability/Equity/Revenue/Expense)", "الطبيعة (Debit/Credit)", "كود الأب", "يقبل ترحيل (نعم/لا)" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
            cell.Style.Font.FontColor = XLColor.White;
        }

        // Example Row
        ws.Cell(2,1).Value = "110101";
        ws.Cell(2,2).Value = "خزينة المكتب";
        ws.Cell(2,3).Value = "Office Cash";
        ws.Cell(2,4).Value = "Asset";
        ws.Cell(2,5).Value = "Debit";
        ws.Cell(2,6).Value = "1101";
        ws.Cell(2,7).Value = "نعم";

        ws.Columns().AdjustToContents();
        var stream = new MemoryStream();
        wb.SaveAs(stream); stream.Position = 0;
        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "accounts_import_template.xlsx");
    }

    // ── IMPORT ACCOUNTS ───────────────────────────────────────
    [HttpPost("accounts")]
    public async Task<IActionResult> ImportAccounts(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "لم يتم رفع ملف" });

        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            var added = 0;
            var updated = 0;
            var errors = new List<string>();

            var allAccounts = await _db.Accounts.ToListAsync();
            var accountMap = allAccounts.ToDictionary(a => a.Code, a => a, StringComparer.OrdinalIgnoreCase);

            var rows = new List<dynamic>();
            for (int r = 2; r <= lastRow; r++)
            {
                var code = ws.Cell(r, 1).GetString().Trim();
                if (string.IsNullOrEmpty(code)) continue;

                rows.Add(new {
                    Row = r, Code = code,
                    NameAr = ws.Cell(r, 2).GetString().Trim(),
                    NameEn = ws.Cell(r, 3).GetString().Trim(),
                    TypeStr = ws.Cell(r, 4).GetString().Trim(),
                    NatureStr = ws.Cell(r, 5).GetString().Trim(),
                    ParentCode = ws.Cell(r, 6).GetString().Trim(),
                    AllowPosting = ws.Cell(r, 7).GetString().Trim().Contains("نعم")
                });
            }

            // Pass 1: Upsert
            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.NameAr)) { errors.Add($"صف {row.Row}: الاسم مطلوب"); continue; }
                
                if (!Enum.TryParse<AccountType>(row.TypeStr as string, true, out AccountType type)) type = AccountType.Asset;
                if (!Enum.TryParse<AccountNature>(row.NatureStr as string, true, out AccountNature nature)) nature = AccountNature.Debit;

                if (accountMap.TryGetValue(row.Code as string ?? "", out var existing))
                {
                    existing.NameAr = row.NameAr;
                    existing.NameEn = row.NameEn;
                    existing.Type = type;
                    existing.Nature = nature;
                    existing.AllowPosting = row.AllowPosting;
                    updated++;
                }
                else
                {
                    var newAcc = new Account {
                        Code = row.Code, NameAr = row.NameAr, NameEn = row.NameEn,
                        Type = type, Nature = nature, AllowPosting = row.AllowPosting,
                        IsActive = true, CreatedAt = TimeHelper.GetEgyptTime()
                    };
                    _db.Accounts.Add(newAcc);
                    accountMap[row.Code] = newAcc;
                    added++;
                }
            }
            await _db.SaveChangesAsync();

            // Pass 2: Parents
            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.ParentCode as string)) continue;
                if (accountMap.TryGetValue(row.Code as string ?? "", out var acc) && accountMap.TryGetValue(row.ParentCode as string ?? "", out var parent))
                {
                    acc.ParentId = parent.Id;
                }
            }
            await _db.SaveChangesAsync();

            // Fix Tree Structure (Levels, IsLeaf)
            var accountsToFix = await _db.Accounts.ToListAsync();
            void UpdateLevels(int? pId, int lvl) {
                var kids = accountsToFix.Where(a => a.ParentId == pId).ToList();
                foreach (var k in kids) {
                    k.Level = lvl;
                    k.IsLeaf = !accountsToFix.Any(a => a.ParentId == k.Id);
                    UpdateLevels(k.Id, lvl + 1);
                }
            }
            UpdateLevels(null, 1);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, added, updated, errors });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"خطأ في المعالجة: {ex.Message}" });
        }
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
