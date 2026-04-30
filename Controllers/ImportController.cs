using Sportive.API.Interfaces;
﻿using Sportive.API.Attributes;
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
[RequirePermission(ModuleKeys.Import, requireEdit: true)]
public class ImportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITranslator _t;
    public ImportController(AppDbContext db, ITranslator t) { _db = db; _t = t; }
    public ImportController(AppDbContext db) => _db = db;

    // â”€â”€ TEMPLATE DOWNLOAD â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // GET /api/import/template
    // â”€â”€ TEMPLATE DOWNLOAD â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // GET /api/import/template
    [HttpGet("template")]
    public async Task<IActionResult> GetTemplate()
    {
        try
        {
            var allCats = await _db.Categories.AsNoTracking().Where(c => c.NameAr != null).ToListAsync();
            foreach (var c in allCats) c.NameAr = c.NameAr.Trim();

            var mainCats     = allCats.Where(c => c.ParentId == null).OrderBy(c => c.NameAr).ToList();
            var brands       = await _db.Brands.AsNoTracking().Where(b => b.NameAr != null).Select(b => b.NameAr!).ToListAsync();
            var units        = await _db.ProductUnits.AsNoTracking().Where(u => u.NameAr != null).Select(u => u.NameAr!).ToListAsync();
            
            // Fetch existing sizes and colors to provide as options â€” Limited for performance
            var existingSizes  = await _db.ProductVariants.AsNoTracking().Where(v => v.Size != null).Select(v => v.Size!).Distinct().Take(100).ToListAsync();
            var catNames = allCats.Select(c => c.NameAr).Where(n => n != null).ToHashSet();
            var existingColors = await _db.ProductVariants.AsNoTracking()
                .Where(v => v.ColorAr != null)
                .Select(v => v.ColorAr!)
                .Distinct()
                .Take(100)
                .ToListAsync();
            
            // Filter out colors that are actually category names or too long
            existingColors = existingColors
                .Where(c => !catNames.Contains(c) && c.Length < 25)
                .ToList();

            var standardColors = new List<string> { 
                "Ø£Ø¨ÙŠØ¶", "Ø£Ø³ÙˆØ¯", "Ø£Ø­Ù…Ø±", "Ø£Ø²Ø±Ù‚", "Ø£Ø®Ø¶Ø±", "Ø£ØµÙØ±", "Ø±Ù…Ø§Ø¯ÙŠ", "ÙƒØ­Ù„ÙŠ", "Ø¨Ù†ÙŠ", "Ø¨ÙŠØ¬", "Ø¨Ø±ØªÙ‚Ø§Ù„ÙŠ", "Ø¨Ù†ÙØ³Ø¬ÙŠ", "Ø³Ù…Ø§ÙˆÙŠ", "Ø°Ù‡Ø¨ÙŠ", "ÙØ¶ÙŠ" 
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

            if (!brands.Any()) brands.Add("عام");
            if (!units.Any()) units.Add("قطعة");
            if (!existingSizes.Any()) existingSizes = new List<string> { "S", "M", "L", "XL", "XXL", "3XL", "Free Size" };
            if (!existingColors.Any()) existingColors = new List<string> { "أبيض", "أسود", "أحمر", "أزرق", "أخضر", "رمادي", "كحلي", "بني" };

            FillCol(2, brands);     
            FillCol(3, units);      
            FillCol(4, new List<string> { "Ù†Ø¹Ù…", "Ù„Ø§" });
            FillCol(5, new List<string> { "Ù†Ø´Ø·", "Ù…Ø³ÙˆØ¯Ø©", "Ù…Ø®ÙÙŠ" });
            FillCol(6, existingSizes);
            FillCol(7, existingColors);

            wb.DefinedNames.Add("BrandsList", wsL.Range(1, 2, Math.Max(1, brands.Count), 2));
            wb.DefinedNames.Add("UnitsList",  wsL.Range(1, 3, Math.Max(1, units.Count), 3));
            wb.DefinedNames.Add("YesNoList",  wsL.Range(1, 4, 2, 4));
            wb.DefinedNames.Add("StatusList", wsL.Range(1, 5, 3, 5));
            wb.DefinedNames.Add("SizesList",  wsL.Range(1, 6, Math.Max(1, existingSizes.Count), 6));
            wb.DefinedNames.Add("ColorsList", wsL.Range(1, 7, Math.Max(1, existingColors.Count), 7));

            // 3. Category Mapping (Cols 10-11)
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
            
            mapping = mapping
                .Where(m => !string.IsNullOrWhiteSpace(m.Parent) && !string.IsNullOrWhiteSpace(m.Child))
                .Select(m => (Parent: m.Parent.Trim(), Child: m.Child.Trim()))
                .Distinct()
                .OrderBy(m => m.Parent)
                .ThenBy(m => m.Child)
                .ToList();
            
            wsL.Cell(1, 10).Value = "__DUMMY__";
            wsL.Cell(1, 11).Value = "(" + _t.Get("Import.SizeHeader") + " " + _t.Get("Accounting.General") + ")";

            for (int i = 0; i < mapping.Count; i++)
            {
                wsL.Cell(i + 2, 10).Value = mapping[i].Parent;
                wsL.Cell(i + 2, 11).Value = mapping[i].Child;
            }
            
            // 4. Size Mapping
            var sizeGroups = await _db.SizeGroups.AsNoTracking().Include(g => g.Values).ToListAsync();
            var catSizeMapping = new List<(string CatName, string SizeValue)>();
            
            foreach (var cat in allCats)
            {
                var targetGroupId = cat.SizeGroupId;
                var current = cat;
                while (targetGroupId == null && current.ParentId != null)
                {
                    current = allCats.FirstOrDefault(c => c.Id == current.ParentId);
                    if (current == null) break;
                    targetGroupId = current.SizeGroupId;
                }
                
                if (targetGroupId != null)
                {
                    var group = sizeGroups.FirstOrDefault(g => g.Id == targetGroupId);
                    if (group != null)
                    {
                        foreach (var val in group.Values.OrderBy(v => v.SortOrder))
                            catSizeMapping.Add((cat.NameAr!, val.Value));
                    }
                }
            }

            catSizeMapping = catSizeMapping
                .Where(m => !string.IsNullOrWhiteSpace(m.CatName) && !string.IsNullOrWhiteSpace(m.SizeValue))
                .OrderBy(m => m.CatName)
                .Distinct()
                .ToList();

            wsL.Cell(1, 14).Value = "__DUMMY__";
            wsL.Cell(1, 15).Value = "(Ø§Ø®ØªØ± Ø§Ù„ØªØµÙ†ÙŠÙ Ø£ÙˆÙ„Ø§Ù‹)";
            for (int i = 0; i < catSizeMapping.Count; i++)
            {
                wsL.Cell(i + 2, 14).Value = catSizeMapping[i].CatName;
                wsL.Cell(i + 2, 15).Value = catSizeMapping[i].SizeValue;
            }
            wb.DefinedNames.Add("SizeParent", wsL.Range(1, 14, Math.Max(1, catSizeMapping.Count + 1), 14));
            wb.DefinedNames.Add("SizeChild",  wsL.Range(1, 15, Math.Max(1, catSizeMapping.Count + 1), 15));
            
            var parentRange = wsL.Range(1, 10, mapping.Count + 1, 10);
            var childRange  = wsL.Range(1, 11, mapping.Count + 1, 11);
            wb.DefinedNames.Add("MapParent", parentRange);
            wb.DefinedNames.Add("MapChild", childRange);

            var ws1 = wb.Worksheets.Add(_t.Get("Import.TemplateProductsSheet"));
            ws1.RightToLeft = true;

            var headers1 = new[] {
                _t.Get("Import.SkuHeader"), _t.Get("Import.NameArHeader"), _t.Get("Import.UnitHeader"), _t.Get("Import.NameEnHeader"),
                _t.Get("Import.MainCatHeader"), _t.Get("Import.SubCatHeader"), _t.Get("Import.SubSubCatHeader"),
                _t.Get("Import.CostHeader"), _t.Get("Import.PriceHeader"), _t.Get("Import.DiscountHeader"),
                _t.Get("Import.TaxableHeader"), _t.Get("Import.BrandHeader"), _t.Get("Import.SizeHeader"), _t.Get("Import.ColorEnHeader"), _t.Get("Import.ColorArHeader"),
                _t.Get("Import.StockHeader"), _t.Get("Import.PriceAdjHeader"), _t.Get("Import.ReorderHeader"), _t.Get("Import.StatusHeader"),
                _t.Get("Import.FeaturedHeader"), _t.Get("Import.DescArHeader"), _t.Get("Import.DescEnHeader")
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

            for (int r = 2; r <= 500; r++) // Reduced to 500 for better performance and reliability
            {
                ws1.Cell(r, 5).CreateDataValidation().List("=MainCategoriesList", true);
                ws1.Cell(r, 6).CreateDataValidation().List("=IF($E" + r + "=\"\", MapChild, OFFSET(MapChild, MATCH($E" + r + ", MapParent, 0)-1, 0, MAX(1, COUNTIF(MapParent, $E" + r + ")), 1))", true);
                ws1.Cell(r, 7).CreateDataValidation().List("=IF($F" + r + "=\"\", MapChild, OFFSET(MapChild, MATCH($F" + r + ", MapParent, 0)-1, 0, MAX(1, COUNTIF(MapParent, $F" + r + ")), 1))", true);
                
                ws1.Cell(r, 12).CreateDataValidation().List("=BrandsList", true);
                ws1.Cell(r, 3).CreateDataValidation().List("=UnitsList", true);
                ws1.Cell(r, 11).CreateDataValidation().List("=YesNoList", true);
                ws1.Cell(r, 19).CreateDataValidation().List("=StatusList", true);
                ws1.Cell(r, 20).CreateDataValidation().List("=YesNoList", true);
                ws1.Cell(r, 15).CreateDataValidation().List("=ColorsList", true);

                var sizeFormula = "=IF(COUNTIF(SizeParent,IF($G" + r + "<>\"\",$G" + r + ",IF($F" + r + "<>\"\",$F" + r + ",$E" + r + ")))>0," +
                                 "OFFSET(SizeChild,MATCH(IF($G" + r + "<>\"\",$G" + r + ",IF($F" + r + "<>\"\",$F" + r + ",$E" + r + ")),SizeParent,0)-1,0," +
                                 "COUNTIF(SizeParent,IF($G" + r + "<>\"\",$G" + r + ",IF($F" + r + "<>\"\",$F" + r + ",$E" + r + "))),1),SizesList)";
                
                ws1.Cell(r, 13).CreateDataValidation().List(sizeFormula, true);
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

            // ws1.Columns().AdjustToContents(); // âŒ REMOVED: Often fails on Linux/Docker without GDI+
            
            var stream = new MemoryStream();
            wb.SaveAs(stream); 
            stream.Position = 0;

            return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "products_import_template.xlsx");
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = _t.Get("Import.TemplateCreateError", ex.Message) });
        }
    }


    // â”€â”€ IMPORT PRODUCTS â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // POST /api/import/products
    [HttpPost("products")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB
    public async Task<IActionResult> ImportProducts(IFormFile file, [FromQuery] bool update = false)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = _t.Get("Import.NoFile") });
            return BadRequest(new { message = "Ù„Ù… ÙŠØªÙ… Ø±ÙØ¹ Ù…Ù„Ù" });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext != ".xlsx" && ext != ".xls")
            return BadRequest(new { message = _t.Get("Import.ExcelOnly") });

        var result = new ImportResult();

        try
        {
            using var stream = file.OpenReadStream();
            using var wb     = new XLWorkbook(stream);

            var ws = wb.Worksheets.FirstOrDefault(x => x.Visibility == XLWorksheetVisibility.Visible)
                     ?? wb.Worksheets.FirstOrDefault();

            if (ws == null) throw new Exception(_t.Get("Import.NoSheets"));

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
            int colSku      = GetCol("Ø§Ù„ÙƒÙˆØ¯ SKU", "Ø§Ù„Ø¨Ø§Ø±ÙƒÙˆØ¯", "sku", "Code");
            int colNameAr   = GetCol("Ø§Ù„Ø§Ø³Ù… Ø¹Ø±Ø¨ÙŠ", "Ø§Ø³Ù… Ø§Ù„Ù…Ù†ØªØ¬", "Ø§Ù„Ø§Ø³Ù…", "Name Ar");
            int colUnit     = GetCol("Ø§Ù„ÙˆØ­Ø¯Ø©", "ÙˆØ­Ø¯Ø© Ø§Ù„Ù‚ÙŠØ§Ø³", "Unit");
            int colNameEn   = GetCol("Ø§Ù„Ø§Ø³Ù… Ø§Ù†Ø¬Ù„ÙŠØ²ÙŠ", "Ø§Ù„Ø§Ø³Ù… English", "Name En");
            int colMainCat  = GetCol("Ø§Ù„ØªØµÙ†ÙŠÙ Ø§Ù„Ø£Ø³Ø§Ø³ÙŠ", "Ø§Ù„ÙØ¦Ø©", "Ø§Ù„ØªØµÙ†ÙŠÙ", "Main Category", "Category");
            int colSubCat   = GetCol("Ø§Ù„ØªØµÙ†ÙŠÙ Ø§Ù„ÙØ±Ø¹ÙŠ", "Ø§Ù„ÙØ¦Ø© Ø§Ù„ÙØ±Ø¹ÙŠØ©", "Sub Category");
            int colSubSub   = GetCol("Ø§Ù„ØªØµÙ†ÙŠÙ Ø§Ù„ÙØ±Ø¹ÙŠ 2", "Ø§Ù„ØªØµÙ†ÙŠÙ ÙØ±Ø¹ ÙØ±Ø¹ÙŠ", "Ø§Ù„ÙØ¦Ø© Ø§Ù„ÙØ±Ø¹ÙŠØ© 2", "Sub Sub Category");
            int colCost     = GetCol("Ø³Ø¹Ø± Ø§Ù„ØªÙƒÙ„ÙØ©", "Ø§Ù„ØªÙƒÙ„ÙØ©", "Cost");
            int colPrice    = GetCol("Ø§Ù„Ø³Ø¹Ø±", "Ø³Ø¹Ø± Ø§Ù„Ø¨ÙŠØ¹", "Price");
            int colDisc     = GetCol("Ø³Ø¹Ø± Ø§Ù„Ø®ØµÙ…", "Ø§Ù„Ø®ØµÙ…", "Discount");
            int colHasTax   = GetCol("Ø®Ø§Ø¶Ø¹ Ù„Ù„Ø¶Ø±ÙŠØ¨Ø©", "taxable", "Ø§Ù„Ø¶Ø±ÙŠØ¨Ø©", "Is Taxable"); 
            int colBrand    = GetCol("Ø§Ù„Ø¹Ù„Ø§Ù…Ø© Ø§Ù„ØªØ¬Ø§Ø±ÙŠØ©", "Ø§Ù„Ù…Ø§Ø±ÙƒØ©", "Brand", "Ø§Ù„Ù…Ø§Ø±ÙƒÙ‡");
            int colSize     = GetCol("Ø§Ù„Ù…Ù‚Ø§Ø³", "Size", "Ø§Ù„Ù‚ÙŠØ§Ø³", "Ø§Ù„Ù…Ù‚Ø§Ø³Ø§Øª", "size");
            int colColorEn  = GetCol("Ø§Ù„Ù„ÙˆÙ† (English)", "Ø§Ù„Ù„ÙˆÙ† English", "Color En", "Color");
            int colColorAr  = GetCol("Ø§Ù„Ù„ÙˆÙ† (Ø¹Ø±Ø¨ÙŠ)", "Ø§Ù„Ù„ÙˆÙ† Ø¹Ø±Ø¨ÙŠ", "Color Ar", "Ø§Ù„Ù„ÙˆÙ†", "Ø§Ù„ÙˆØ§Ù†");
            int colStock    = GetCol("Ø§Ù„Ù…Ø®Ø²ÙˆÙ†", "Stock", "Ø§Ù„ÙƒÙ…ÙŠØ©");
            int colAdj      = GetCol("ÙØ§Ø±Ù‚ Ø§Ù„Ø³Ø¹Ø± Ù„Ù„Ù…Ù‚Ø§Ø³", "Price Adjustment");
            int colReorder  = GetCol("Ø­Ø¯ Ø§Ù„Ø·Ù„Ø¨", "Reorder Level");
            int colStatus   = GetCol("Ø§Ù„Ø­Ø§Ù„Ø©", "Status");
            int colFeat     = GetCol("Ù…Ù…ÙŠØ² (Ù†Ø¹Ù…/Ù„Ø§)", "Featured");
            int colDescAr   = GetCol("Ø§Ù„ÙˆØµÙ Ø¹Ø±Ø¨ÙŠ", "Description Ar");
            int colDescEn   = GetCol("Ø§Ù„ÙˆØµÙ Ø§Ù†Ø¬Ù„ÙŠØ²ÙŠ", "Description En");

            if (colSku == -1 || colNameAr == -1 || colPrice == -1 || colUnit == -1 || colMainCat == -1)
                return BadRequest(new { message = _t.Get("Import.MissingColumns") });

            var categories   = await _db.Categories.AsNoTracking().ToListAsync();
            var brands       = await _db.Brands.AsNoTracking().ToListAsync();
            var units        = await _db.ProductUnits.AsNoTracking().ToListAsync();
            var skuList      = await _db.Products.AsNoTracking().Select(p => p.SKU).ToListAsync();
            var existingSkus = skuList.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var productsDict = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            // Prepare Error Workbook for rejected rows
            using var errorWb = new XLWorkbook();
            var errorWs = errorWb.Worksheets.Add(_t.Get("Import.RejectedRowsSheet"));
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
            errorWs.Cell(1, colErrDesc).Value = _t.Get("Import.RejectionReasonHeader");
            errorWs.Cell(1, colErrDesc).Style.Fill.BackgroundColor = XLColor.Red;
            errorWs.Cell(1, colErrDesc).Style.Font.FontColor = XLColor.White;

            int errorRowIdx = 2;

            void LogRowError(int r, string message)
            {
                result.Errors.Add($"ØµÙ {r}: {message}");
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
                    if (!result.Skipped.Any(s => s.Contains($"Ø§Ù„ÙƒÙˆØ¯ '{sku}' Ù…ÙˆØ¬ÙˆØ¯")))
                    if (!result.Skipped.Any(s => s.Contains(sku)))
                        result.Skipped.Add(_t.Get("Import.SkuExists", sku));
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

                                product.HasTax = !GetVal(colHasTax).Contains("Ù„Ø§");
                                product.ReorderLevel = int.TryParse(GetVal(colReorder), out var rl) ? rl : product.ReorderLevel;
                                product.Status = GetVal(colStatus) switch { "Ù…Ø³ÙˆØ¯Ø©" => ProductStatus.Draft, "Ù…Ø®ÙÙŠ" => ProductStatus.Hidden, _ => ProductStatus.Active };
                                product.IsFeatured = GetVal(colFeat).Contains("Ù†Ø¹Ù…");
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
                            LogRowError(r, _t.Get("Import.RowMandatoryMissing", nameAr, priceStr, unitStr, mainC));
                            continue;
                        }

                        if (!decimal.TryParse(priceStr, out var price))
                        {
                            LogRowError(r, _t.Get("Import.InvalidPrice", priceStr, sku));
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
                            LogRowError(r, _t.Get("Import.CategoryNotFound", mainC));
                            continue;
                        }

                        var bStr = GetVal(colBrand);
                        int? bId = brands.FirstOrDefault(b => string.Equals((b.NameAr ?? "").Trim(), bStr.Trim(), StringComparison.OrdinalIgnoreCase) || b.Id.ToString() == bStr)?.Id;

                        int? uId = units.FirstOrDefault(u => string.Equals((u.NameAr ?? "").Trim(), unitStr.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
                        if (uId == null)
                        {
                            LogRowError(r, _t.Get("Import.UnitNotFound", unitStr));
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
                            HasTax = !GetVal(colHasTax).Contains("Ù„Ø§"),
                            ReorderLevel = int.TryParse(GetVal(colReorder), out var rl) ? rl : 0,
                            DescriptionAr = GetVal(colDescAr).NullIfEmpty(),
                            DescriptionEn = GetVal(colDescEn).NullIfEmpty(),
                            IsFeatured    = GetVal(colFeat).Contains("Ù†Ø¹Ù…"),
                            Status = GetVal(colStatus) switch { "Ù…Ø³ÙˆØ¯Ø©" => ProductStatus.Draft, "Ù…Ø®ÙÙŠ" => ProductStatus.Hidden, _ => ProductStatus.Active },
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
                // Removed AdjustToContents for Linux stability
                using var errStream = new MemoryStream();
                errorWb.SaveAs(errStream);
                errorReportBase64 = Convert.ToBase64String(errStream.ToArray());
            }

            return Ok(new { 
                message = result.Errors.Any() ? _t.Get("Import.ImportWithErrors") : _t.Get("Import.ImportSuccess"),
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
            return BadRequest(new { message = _t.Get("Accounting.ProcessingError", ex.Message) });
        }
    }


    // â”€â”€ INVENTORY TEMPLATE â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // GET /api/import/inventory-template
    [HttpGet("inventory-template")]
    public async Task<IActionResult> GetInventoryTemplate()
    {
        using var wb = new XLWorkbook();

        // Sheet 1: Stock Update
        var ws = wb.Worksheets.Add(_t.Get("Import.InventoryUpdateSheet"));
        ws.RightToLeft = true;

        var headers = new[] { _t.Get("Import.SkuHeader").Replace("*","").Trim(), _t.Get("Import.NameArHeader").Replace("*","").Trim(), _t.Get("Import.SizeHeader"), _t.Get("Import.ColorArHeader"), _t.Get("Import.NewQtyHeader") };
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
        ws.Cell(2, 1).Value = _t.Get("Import.InventoryInstruction");
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

        // Removed AdjustToContents for Linux stability

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;

        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "inventory_import_template.xlsx");
    }

    // â”€â”€ IMPORT INVENTORY (STOCK UPDATE ONLY) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // POST /api/import/inventory
    [HttpPost("inventory")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ImportInventory(IFormFile file)
    {
        if (file == null || file.Length == 0)
        if (file == null || file.Length == 0) return BadRequest(new { message = _t.Get("Import.NoFile") });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext != ".xlsx" && ext != ".xls")
            return BadRequest(new { message = _t.Get("Import.ExcelOnly") });

        int updated = 0;
        var skipped = new List<string>();
        var errors  = new List<string>();

        try
        {
            using var stream = file.OpenReadStream();
            using var wb     = new XLWorkbook(stream);

            if (!wb.TryGetWorksheet(_t.Get("Import.InventoryUpdateSheet"), out var ws)) ws = wb.Worksheets.First();
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
                    errors.Add(_t.Get("Import.InvalidQty", sku));
                    continue;
                }

                if (!productBySku.TryGetValue(sku, out var product))
                {
                    skipped.Add(_t.Get("Import.SkuNotFound", sku));
                    continue;
                }

                var activeVariants = product.Variants.ToList();

                if (!activeVariants.Any())
                {
                    // Product without variants â€” update directly
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
                        skipped.Add(_t.Get("Import.SizeColorNotFound", size, color, sku));
                        continue;
                    }

                    variant.StockQuantity = qty;

                    // Recalculate product total stock
                    product.TotalStock = activeVariants.Sum(v => v.StockQuantity);
                    updated++;
                }
                else
                {
                    // SKU has variants but no size/color specified â€” skip with helpful message
                    skipped.Add(_t.Get("Import.SkuHasVariantsMustSpecify", sku));
                    continue;
                }
            }

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Ø®Ø·Ø£ ÙÙŠ Ù‚Ø±Ø§Ø¡Ø© Ø§Ù„Ù…Ù„Ù: {ex.Message}" });
        }

        return Ok(new
        {
            message  = _t.Get("Import.InventoryUpdateSuccess", updated),
            updated,
            skipped  = skipped.Count,
            errors   = errors.Count,
            details  = new { skipped, errors }
        });
    }

    // â”€â”€ ACCOUNTS TEMPLATE â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [HttpGet("accounts-template")]
    public IActionResult GetAccountsTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(_t.Get("Import.AccountsSheet"));
        ws.RightToLeft = true;

        var headers = new[] { _t.Get("Accounting.AccountCodeHeader"), _t.Get("Import.NameArHeader"), _t.Get("Import.NameEnHeader"), _t.Get("Accounting.TypeHeader"), _t.Get("Accounting.NatureHeader"), "Parent Code", "Accept Posting" };
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
        ws.Cell(2,2).Value = "Ø®Ø²ÙŠÙ†Ø© Ø§Ù„Ù…ÙƒØªØ¨";
        ws.Cell(2,3).Value = "Office Cash";
        ws.Cell(2,4).Value = "Asset";
        ws.Cell(2,5).Value = "Debit";
        ws.Cell(2,6).Value = "1101";
        ws.Cell(2,7).Value = "Ù†Ø¹Ù…";

        // Removed AdjustToContents for Linux stability
        var stream = new MemoryStream();
        wb.SaveAs(stream); stream.Position = 0;
        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "accounts_import_template.xlsx");
    }

    // â”€â”€ IMPORT ACCOUNTS â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    [HttpPost("accounts")]
    public async Task<IActionResult> ImportAccounts(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "Ù„Ù… ÙŠØªÙ… Ø±ÙØ¹ Ù…Ù„Ù" });

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
                    AllowPosting = ws.Cell(r, 7).GetString().Trim().Contains("Ù†Ø¹Ù…")
                });
            }

            // Pass 1: Upsert
            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.NameAr)) { errors.Add($"ØµÙ {row.Row}: Ø§Ù„Ø§Ø³Ù… Ù…Ø·Ù„ÙˆØ¨"); continue; }
                
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
            return BadRequest(new { message = $"Ø®Ø·Ø£ ÙÙŠ Ø§Ù„Ù…Ø¹Ø§Ù„Ø¬Ø©: {ex.Message}" });
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

