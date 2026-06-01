using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;
using System.Security.Claims;
using ClosedXML.Excel;
using System.IO;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.InventoryOpening)]
public class InventoryOpeningBalanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SequenceService _seq;
    private readonly IAccountingService _accounting;
    private readonly IInventoryService _inventory;
    private readonly AccountingCoreService _core;
    private readonly IPdfService _pdf;
    private readonly ITranslator _t;

    public InventoryOpeningBalanceController(
        AppDbContext db, 
        SequenceService seq, 
        IAccountingService accounting,
        IInventoryService inventory,
        AccountingCoreService core,
        IPdfService pdf,
        ITranslator t)
    {
        _db = db;
        _seq = seq;
        _accounting = accounting;
        _inventory = inventory;
        _core = core;
        _pdf = pdf;
        _t = t;
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetPdf(int id)
    {
        var op = await _db.InventoryOpeningBalances.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (op == null) return NotFound();

        var pdfBytes = await _pdf.GenerateOpeningBalancePdfAsync(op);
        return File(pdfBytes, "application/pdf", $"OpeningBalance-{op.Reference}.pdf");
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = _db.InventoryOpeningBalances.AsNoTracking();
        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(x => x.Date)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new OpeningBalanceSummaryDto(
                x.Id, x.Reference, x.Date, x.TotalValue, x.Items.Count, x.CostCenter
            ))
            .ToListAsync();

        return Ok(new PaginatedResult<OpeningBalanceSummaryDto>(items, total, page, pageSize, (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var x = await _db.InventoryOpeningBalances
            .Include(b => b.Items).ThenInclude(i => i.Product).ThenInclude(p => p != null ? p.Images : null)
            .Include(b => b.Items).ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (x == null) return NotFound();

        return Ok(new OpeningBalanceDetailDto(
            x.Id, x.Reference, x.Date, x.Notes, x.TotalValue,
                        x.Items.Select(i => new OpeningBalanceItemDto(
                i.Id, i.ProductId, i.Product?.NameAr, i.Product?.SKU,
                i.ProductVariantId, i.ProductVariant?.Size, i.ProductVariant?.ColorAr,
                i.Quantity, i.CostPrice, i.TotalCost,
                (i.ProductVariantId.HasValue && i.ProductVariant != null && !string.IsNullOrEmpty(i.ProductVariant.ImageUrl))
                    ? i.ProductVariant.ImageUrl 
                    : i.Product?.Images?.FirstOrDefault(img => img.IsMain)?.ImageUrl ?? i.Product?.Images?.FirstOrDefault()?.ImageUrl
            )).ToList(),
            x.CostCenter
        ));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOpeningBalanceDto dto)
    {
        await _core.CheckDateLockAsync(dto.Date, User);

        // 🚨 PREVENT DUPLICATES: التأكد من عدم وجود رصيد افتتاحي سابق للمخزون في هذه الفترة
        if (await _db.InventoryOpeningBalances.AnyAsync(x => x.Date.Year == dto.Date.Year))
            return BadRequest(new { message = _t.Get("Inventory.OpeningBalanceExists") });

        if (dto.Items == null || !dto.Items.Any())
            return BadRequest(new { message = _t.Get("Purchases.MinOneItemRequired") });

        var refNo = await _seq.NextAsync("OB");

        var ob = new InventoryOpeningBalance
        {
            Reference = refNo,
            Date      = dto.Date,
            Notes     = dto.Notes,
            CostCenter = dto.CostCenter,
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        decimal totalValue = 0;
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        foreach (var item in dto.Items)
        {
            var total = item.Quantity * item.CostPrice;
            ob.Items.Add(new InventoryOpeningBalanceItem
            {
                ProductId = item.ProductId,
                ProductVariantId = item.ProductVariantId,
                Quantity = item.Quantity,
                CostPrice = item.CostPrice,
                CreatedAt = TimeHelper.GetEgyptTime()
            });
            totalValue += total;

            // 1. Update Inventory (OPTIMIZED: No auto-save, no broadcast inside loop)
            if (item.ProductId.HasValue)
            {
                await _inventory.LogMovementAsync(
                    InventoryMovementType.OpeningBalance, 
                    item.Quantity, 
                    item.ProductId, 
                    item.ProductVariantId, 
                    refNo, 
                    _t.Get("Inventory.OpeningBalanceLog"), 
                    userId,
                    item.CostPrice,
                    ob.CostCenter,
                    autoSave: false,
                    broadcast: false,
                    date: ob.Date
                );

                // 2. Update Product Cost Price if requested
                // Note: We avoid FindAsync here to keep it fast
                var product = await _db.Products.FindAsync(item.ProductId.Value);
                if (product != null && dto.UpdateProductCost && item.CostPrice > 0)
                {
                    product.CostPrice = item.CostPrice;
                    product.UpdatedAt = TimeHelper.GetEgyptTime();
                }
            }
        }

        ob.TotalValue = totalValue;
        _db.InventoryOpeningBalances.Add(ob);
        await _db.SaveChangesAsync();

        // 3. Post Accounting Journal
        await PostJournalAsync(ob);

        return CreatedAtAction(nameof(GetById), new { id = ob.Id }, new { id = ob.Id, reference = ob.Reference });
    }

    [HttpGet("{id}/excel")]
    public async Task<IActionResult> ExportToExcel(int id)
    {
        var ob = await _db.InventoryOpeningBalances
            .Include(x => x.Items).ThenInclude(i => i.Product)
            .Include(x => x.Items).ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (ob == null) return NotFound();

        using (var workbook = new ClosedXML.Excel.XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Opening Balance");
            sheet.RightToLeft = true;

            // Header Info
            sheet.Cell(1, 1).Value = _t.Get("Inventory.ReferenceLabel");
            sheet.Cell(1, 2).Value = ob.Reference;
            sheet.Cell(2, 1).Value = _t.Get("Inventory.DateLabel");
            sheet.Cell(2, 2).Value = ob.Date.ToShortDateString();
            sheet.Cell(3, 1).Value = _t.Get("Inventory.TotalLabel");
            sheet.Cell(3, 2).Value = ob.TotalValue;
            sheet.Cell(3, 2).Style.NumberFormat.Format = "#,##0.00";

            // Items Table
            var tableRange = sheet.Range(5, 1, 5, 6);
            sheet.Cell(5, 1).Value = _t.Get("Inventory.ItemLabel");
            sheet.Cell(5, 2).Value = _t.Get("Inventory.SkuHeader");
            sheet.Cell(5, 3).Value = _t.Get("Inventory.SizeColorHeader");
            sheet.Cell(5, 4).Value = _t.Get("Inventory.QtyHeader");
            sheet.Cell(5, 5).Value = _t.Get("Inventory.CostHeader");
            sheet.Cell(5, 6).Value = _t.Get("Inventory.TotalHeader");
            sheet.Range(5, 1, 5, 6).Style.Font.Bold = true;
            sheet.Range(5, 1, 5, 6).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;

            int row = 6;
            foreach (var item in ob.Items)
            {
                sheet.Cell(row, 1).Value = item.Product?.NameAr;
                sheet.Cell(row, 2).Value = item.Product?.SKU;
                sheet.Cell(row, 3).Value = $"{item.ProductVariant?.Size} {item.ProductVariant?.ColorAr}".Trim();
                sheet.Cell(row, 4).Value = item.Quantity;
                sheet.Cell(row, 5).Value = item.CostPrice;
                sheet.Cell(row, 6).Value = item.TotalCost;
                row++;
            }

            if (row > 6)
            {
                sheet.Range(5, 1, row - 1, 6).SetAutoFilter();
            }
            sheet.Columns().AdjustToContents();

            using (var stream = new System.IO.MemoryStream())
            {
                workbook.SaveAs(stream);
                return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"OB-{ob.Reference}.xlsx");
            }
        }
    }

        [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateOpeningBalanceDto dto)
    {
        var ob = await _db.InventoryOpeningBalances
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (ob == null) return NotFound();

        await _core.CheckDateLockAsync(ob.Date, User);
        await _core.CheckDateLockAsync(dto.Date, User);

        if (await _db.InventoryOpeningBalances.AnyAsync(x => x.Date.Year == dto.Date.Year && x.Id != id))
            return BadRequest(new { message = _t.Get("Inventory.OpeningBalanceExists") });

        if (dto.Items == null || !dto.Items.Any())
            return BadRequest(new { message = _t.Get("Purchases.MinOneItemRequired") });

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        // 1. Calculate Deltas to avoid redundant movements and validation issues
        var oldItems = ob.Items.ToList();
        var newItems = dto.Items.ToList();

        // Track items by ProductId + VariantId
        var allKeys = oldItems.Select(i => (i.ProductId, i.ProductVariantId))
            .Union(newItems.Select(i => (i.ProductId, i.ProductVariantId)))
            .Distinct();

        foreach (var key in allKeys)
        {
            var oldQty = oldItems.Where(i => i.ProductId == key.ProductId && i.ProductVariantId == key.ProductVariantId).Sum(i => i.Quantity);
            var newQty = newItems.Where(i => i.ProductId == key.ProductId && i.ProductVariantId == key.ProductVariantId).Sum(i => i.Quantity);
            var delta = newQty - oldQty;

            if (delta != 0 && key.ProductId.HasValue)
            {
                var newCost = newItems.FirstOrDefault(i => i.ProductId == key.ProductId && i.ProductVariantId == key.ProductVariantId)?.CostPrice ?? 0;

                // We use 'force: true' because opening balance corrections should always be allowed
                // regardless of current stock levels (they are correcting the baseline).
                await _inventory.LogMovementAsync(
                    InventoryMovementType.OpeningBalance,
                    delta,
                    key.ProductId,
                    key.ProductVariantId,
                    ob.Reference,
                    _t.Get("Inventory.OpeningBalanceLog"),
                    userId,
                    newCost,
                    ob.CostCenter,
                    autoSave: false,
                    broadcast: true,
                    force: true,
                    date: dto.Date
                );
            }
        }

        // 2. Handle Journal Reversal/Update
        var journal = await _db.JournalEntries.FirstOrDefaultAsync(j => j.Reference == ob.Reference);
        if (journal != null)
        {
            if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin")) 
                _db.JournalEntries.Remove(journal);
            else 
                await _accounting.ReverseEntryAsync(journal.Id, _t.Get("Inventory.OpeningBalanceCancelLog", ob.Reference));
        }

        // 3. Update Record
        ob.Date = dto.Date;
        ob.Notes = dto.Notes;
        ob.CostCenter = dto.CostCenter;

        _db.InventoryOpeningBalanceItems.RemoveRange(ob.Items);
        ob.Items.Clear();

        decimal totalValue = 0;
        foreach (var item in dto.Items)
        {
            var total = item.Quantity * item.CostPrice;
            ob.Items.Add(new InventoryOpeningBalanceItem
            {
                ProductId = item.ProductId,
                ProductVariantId = item.ProductVariantId,
                Quantity = item.Quantity,
                CostPrice = item.CostPrice,
                CreatedAt = TimeHelper.GetEgyptTime()
            });
            totalValue += total;
        }
        
        ob.TotalValue = totalValue;

        await _db.SaveChangesAsync();
        await PostJournalAsync(ob);

        return NoContent();
    }

[HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var ob = await _db.InventoryOpeningBalances
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (ob == null) return NotFound();

        await _core.CheckDateLockAsync(ob.Date, User);

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // 🚨 PRO-ACCOUNTING: لا يجوز حذف قيد مرحل، يجب عكسه
        var journal = await _db.JournalEntries.FirstOrDefaultAsync(j => j.Reference == ob.Reference);
        if (journal != null)
        {
            if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin")) 
            {
                _db.JournalEntries.Remove(journal); // الأدمن يحق له الحذف النهائي
            }
            else 
            {
                await _accounting.ReverseEntryAsync(journal.Id, _t.Get("Inventory.OpeningBalanceCancelLog", ob.Reference));
            }
        }

        // 1. Reverse Inventory Movements
        foreach (var item in ob.Items)
        {
            if (item.ProductId.HasValue)
            {
                await _inventory.LogMovementAsync(
                    InventoryMovementType.Adjustment, // Using Adjustment to reverse
                    -item.Quantity, 
                    item.ProductId, 
                    item.ProductVariantId, 
                    ob.Reference, 
                    _t.Get("Inventory.OpeningBalanceCancelInvLog", ob.Reference), 
                    userId,
                    item.CostPrice,
                    ob.CostCenter,
                    autoSave: false,
                    broadcast: true,
                    force: true
                );
            }
        }

        // 2. Void/Delete Journal Entry - Handled above in pro-accounting block

        // 3. Remove Opening Balance record
        _db.InventoryOpeningBalances.Remove(ob);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("template")]
    public async Task<IActionResult> GetTemplate()
    {
        var existingSizes  = await _db.ProductVariants
            .Where(v => v.Size != null && v.Size != "")
            .Select(v => v.Size!)
            .Distinct()
            .ToListAsync();
            
        var existingColors = await _db.ProductVariants
            .Where(v => v.ColorAr != null && v.ColorAr != "")
            .Select(v => v.ColorAr!)
            .Distinct()
            .ToListAsync();

        var products = await _db.Products
            .AsNoTracking()
            .Select(p => new { p.SKU, p.NameAr })
            .Where(p => p.SKU != null && p.SKU != "")
            .OrderBy(p => p.SKU)
            .ToListAsync();

        using var wb = new XLWorkbook();
        
        // 1. Main Input Sheet (Must be FIRST for reliable import)
        var ws = wb.Worksheets.Add(_t.Get("Inventory.OpeningBalanceSheet") ?? "Opening Balance");
        ws.RightToLeft = true;

        var headers = new[] { 
            _t.Get("Inventory.SkuCol"), 
            _t.Get("Inventory.QtyCol"), 
            _t.Get("Inventory.CostCol"), 
            _t.Get("Inventory.SizeCol"), 
            _t.Get("Inventory.ColorCol"), 
            _t.Get("Inventory.NameCol") 
        };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // 2. Lists Sheet (Hidden)
        var wsL = wb.Worksheets.Add("Lists");
        wsL.Hide();

        void FillCol(int col, List<string> items, string name) {
            if (!items.Any()) items.Add("—");
            for (int i = 0; i < items.Count; i++) wsL.Cell(i + 1, col).Value = items[i];
            var range = wsL.Range(1, col, items.Count, col);
            wb.DefinedNames.Add(name, range);
        }
        FillCol(1, existingSizes, "SizesList");
        FillCol(2, existingColors, "ColorsList");
        
        // 3. Products Reference Sheet
        var wsP = wb.Worksheets.Add(_t.Get("Inventory.ProductsListSheet") ?? "Products Reference");
        wsP.RightToLeft = true;
        wsP.Cell(1, 1).Value = _t.Get("Inventory.SkuCol");
        wsP.Cell(1, 2).Value = _t.Get("Inventory.NameCol");
        wsP.Range(1, 1, 1, 2).Style.Font.Bold = true;
        wsP.Range(1, 1, 1, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#eeeeee");
        
        for (int i = 0; i < products.Count; i++)
        {
            wsP.Cell(i + 2, 1).Value = products[i].SKU;
            wsP.Cell(i + 2, 2).Value = products[i].NameAr;
        }
        wsP.Columns().AdjustToContents();


        // Add Data Validation for 1000 rows
        for (int r = 2; r <= 1001; r++)
        {
            ws.Cell(r, 4).GetDataValidation().List("=SizesList");
            ws.Cell(r, 5).GetDataValidation().List("=ColorsList");
        }

        // Instructions
        ws.Cell(1, 8).Value = "تعليمات الهامش:";
        ws.Cell(1, 8).Style.Font.Bold = true;
        ws.Cell(2, 8).Value = "1. استخدم شيت 'Products Reference' للبحث عن أكواد المنتجات الصحيحة.";
        ws.Cell(3, 8).Value = "2. الكمية والتكلفة يجب أن تكون أرقاماً صحيحة.";
        ws.Cell(4, 8).Value = "3. المقاس واللون اختياريان ولكن يفضل اختيارهما من القائمة المنسدلة.";

        // Sample Data
        ws.Cell(2, 1).Value = products.FirstOrDefault()?.SKU ?? "SKU-XYZ";
        ws.Cell(2, 2).Value = 1;
        ws.Cell(2, 3).Value = 100.00;
        ws.Cell(2, 4).Value = existingSizes.FirstOrDefault() ?? "—";
        ws.Cell(2, 5).Value = existingColors.FirstOrDefault() ?? "—";
        ws.Cell(2, 6).Value = products.FirstOrDefault()?.NameAr ?? "اسم المنتج التوضيحي";
        ws.Row(2).Style.Font.FontColor = XLColor.Gray;

        ws.Columns().AdjustToContents();
        ws.Column(1).Width = 20;
        ws.Column(6).Width = 30;
        ws.Column(8).Width = 50;

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "InventoryOpeningBalance_Template.xlsx");
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = _t.Get("Accounting.NoFileUploaded") });

        var items = new List<dynamic>();
        var errors = new List<string>();

        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            // Try to find the input sheet by common keywords if the first sheet is not the one
            var ws = wb.Worksheets.FirstOrDefault(s => s.Name.Contains("الأرصدة") || s.Name.Contains("Opening")) ?? wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            var products = await _db.Products.Include(p => p.Variants).Include(p => p.Images).ToListAsync();
            
            var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var debugHeaders = new List<string>();
            IXLRow? headerRow = null;

            string Normalize(string s) {
                if (string.IsNullOrEmpty(s)) return "";
                var clean = new string(s.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToLower();
                return clean
                    .Replace("أ", "ا").Replace("إ", "ا").Replace("آ", "ا")
                    .Replace("ة", "ه")
                    .Replace("ى", "ي");
            }

            // --- 💡 AUTO-DETECT HEADER ROW (Search first 10 rows) ---
            var keywords = new[] { "sku", "الكود", "الكمية", "qty", "التكلفة", "cost" };
            for (int r = 1; r <= 10; r++)
            {
                var row = ws.Row(r);
                bool found = false;
                for (int c = 1; c <= 20; c++)
                {
                    var val = Normalize(row.Cell(c).GetString());
                    if (keywords.Any(k => val.Contains(Normalize(k))))
                    {
                        found = true;
                        break;
                    }
                }
                if (found)
                {
                    headerRow = row;
                    break;
                }
            }

            if (headerRow == null) 
                return BadRequest(new { message = _t.Get("Inventory.ImportErrorCols") + " (لم يتم العثور على سطر العناوين)" });

            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 10;
            for (int c = 1; c <= lastCol; c++)
            {
                var hRaw = headerRow.Cell(c).GetString().Trim();
                if (string.IsNullOrEmpty(hRaw)) continue;
                var norm = Normalize(hRaw);
                headers[norm] = c;
                debugHeaders.Add($"{hRaw} -> {norm}");
            }

            int GetCol(params string[] aliases) {
                foreach (var a in aliases) {
                    var normA = Normalize(a);
                    if (string.IsNullOrEmpty(normA)) continue;
                    if (headers.TryGetValue(normA, out var idx)) return idx;
                    foreach (var h in headers) {
                        if (h.Key.Contains(normA) || normA.Contains(h.Key)) return h.Value;
                    }
                }
                return -1;
            }

            int colSku   = GetCol("الكود", "SKU", "Code");
            int colQty   = GetCol("الكمية", "Quantity", "Qty");
            int colCost  = GetCol("التكلفة", "Cost", "Price", "سعر التكلفة");
            int colSize  = GetCol("المقاس", "Size");
            int colColor = GetCol("اللون", "Color");

            if (colSku == -1 || colQty == -1 || colCost == -1)
            {
                var missing = new List<string>();
                if (colSku == -1) missing.Add("الكود (SKU)");
                if (colQty == -1) missing.Add("الكمية (Qty)");
                if (colCost == -1) missing.Add("التكلفة (Cost)");
                
                return BadRequest(new { 
                    message = $"{_t.Get("Inventory.ImportErrorCols")} المفقود: {string.Join(", ", missing)}",
                    detectedHeaders = debugHeaders,
                    headerRowFound = headerRow.RowNumber()
                });
            }

            for (int r = headerRow.RowNumber() + 1; r <= lastRow; r++)
            {
                string GetVal(int col) => col != -1 ? ws.Cell(r, col).GetString().Trim() : "";

                var sku   = GetVal(colSku);
                if (string.IsNullOrEmpty(sku)) continue;

                var qtyStr  = GetVal(colQty);
                var costStr = GetVal(colCost);
                var size    = GetVal(colSize);
                var color   = GetVal(colColor);

                if (!decimal.TryParse(qtyStr, out var qty) || qty <= 0)
                {
                    errors.Add(_t.Get("Inventory.InvalidQtyAt", r, sku));
                    continue;
                }

                if (!decimal.TryParse(costStr, out var cost) || cost < 0)
                {
                    errors.Add(_t.Get("Inventory.InvalidCostAt", r, sku));
                    continue;
                }

                var product = products.FirstOrDefault(p => 
                    string.Equals(p.SKU?.Trim(), sku, StringComparison.OrdinalIgnoreCase));

                if (product == null)
                {
                    errors.Add(_t.Get("Inventory.SkuNotFoundAt", r, sku));
                    continue;
                }

                if (product.Variants.Any())
                {
                    var variant = product.Variants.FirstOrDefault(v => 
                        (string.IsNullOrEmpty(size) || string.Equals(v.Size?.Trim(), size, StringComparison.OrdinalIgnoreCase)) &&
                        (string.IsNullOrEmpty(color) || string.Equals(v.ColorAr?.Trim(), color, StringComparison.OrdinalIgnoreCase) || string.Equals(v.Color?.Trim(), color, StringComparison.OrdinalIgnoreCase)));

                    if (variant != null)
                    {
                        var vImg = variant.ImageUrl ?? 
                                   product.Images.FirstOrDefault(i => i.IsMain)?.ImageUrl ?? 
                                   product.Images.FirstOrDefault()?.ImageUrl;

                        items.Add(new {
                            id        = $"v{variant.Id}",
                            productId = variant.ProductId,
                            variantId = variant.Id,
                            name      = product.NameAr,
                            sku       = product.SKU,
                            size      = variant.Size,
                            color     = variant.ColorAr,
                            image     = vImg,
                            quantity  = (int)qty,
                            costPrice = cost
                        });
                    }
                    else
                    {
                        errors.Add(_t.Get("Inventory.VariantNotFoundAt", r, sku, size, color));
                    }
                }
                else
                {
                    var pImg = product.Images.FirstOrDefault(i => i.IsMain)?.ImageUrl ?? 
                               product.Images.FirstOrDefault()?.ImageUrl;

                    items.Add(new {
                        id        = $"p{product.Id}",
                        productId = product.Id,
                        variantId = (int?)null,
                        name      = product.NameAr,
                        sku       = product.SKU,
                        image     = pImg,
                        quantity  = (int)qty,
                        costPrice = cost
                    });
                }
            }

            if (errors.Any())
            {
                using var errorWb = new XLWorkbook();
                var errorWs = errorWb.Worksheets.Add(_t.Get("Inventory.ImportErrorsSheet"));
                errorWs.RightToLeft = true;
                errorWs.Cell(1, 1).Value = _t.Get("Inventory.RowNoHeader");
                errorWs.Cell(1, 2).Value = _t.Get("Inventory.ErrorDescHeader");
                errorWs.Range(1, 1, 1, 2).Style.Font.Bold = true;
                errorWs.Range(1, 1, 1, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
                errorWs.Range(1, 1, 1, 2).Style.Font.FontColor = XLColor.White;

                for (int i = 0; i < errors.Count; i++)
                {
                    errorWs.Cell(i + 2, 1).Value = _t.Get("Inventory.ErrorRowPrefix", i + 2);
                    errorWs.Cell(i + 2, 2).Value = errors[i];
                }
                errorWs.Columns().AdjustToContents();
                
                using var errStream = new MemoryStream();
                errorWb.SaveAs(errStream);
                return Ok(new { 
                    items, 
                    errors = errors.Count, 
                    details = errors,
                    errorReportFile = Convert.ToBase64String(errStream.ToArray()) 
                });
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = _t.Get("Inventory.ExcelReadError", ex.Message) });
        }

        return Ok(new { items, errors = 0 });
    }

    private async Task PostJournalAsync(InventoryOpeningBalance ob)
    {
        // Get Mapped Accounts - STRICT CHECK
        var inventoryId = await _core.GetRequiredMappedAccountAsync(MappingKeys.Inventory);
        var openingId   = await _core.GetRequiredMappedAccountAsync(MappingKeys.OpeningEquity);

        var dto = new CreateJournalEntryDto(
            EntryDate:   ob.Date,
            Reference:   ob.Reference,
            Description: _t.Get("Inventory.OpeningBalanceJournalDesc", ob.Reference),
            Lines:       new List<CreateJournalLineDto>
            {
                new (inventoryId, ob.TotalValue, 0, _t.Get("Inventory.InventoryOpeningValDesc")),
                new (openingId,   0, ob.TotalValue, _t.Get("Inventory.OpeningBalanceEquityDesc"))
            },
            Type:        JournalEntryType.OpeningBalance,
            CostCenter:  (int?)ob.CostCenter
        );

        await _accounting.PostManualEntryAsync(dto, User);
    }
}


