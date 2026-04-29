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
[Authorize(Roles = "Admin,Manager,Accountant")]
public class InventoryOpeningBalanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SequenceService _seq;
    private readonly IAccountingService _accounting;
    private readonly IInventoryService _inventory;
    private readonly AccountingCoreService _core;
    private readonly IPdfService _pdf;

    public InventoryOpeningBalanceController(
        AppDbContext db, 
        SequenceService seq, 
        IAccountingService accounting,
        IInventoryService inventory,
        AccountingCoreService core,
        IPdfService pdf)
    {
        _db = db;
        _seq = seq;
        _accounting = accounting;
        _inventory = inventory;
        _core = core;
        _pdf = pdf;
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
            .Include(b => b.Items).ThenInclude(i => i.Product)
            .Include(b => b.Items).ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (x == null) return NotFound();

        return Ok(new OpeningBalanceDetailDto(
            x.Id, x.Reference, x.Date, x.Notes, x.TotalValue,
            x.Items.Select(i => new OpeningBalanceItemDto(
                i.Id, i.ProductId, i.Product?.NameAr, i.Product?.SKU,
                i.ProductVariantId, i.ProductVariant?.Size, i.ProductVariant?.ColorAr,
                i.Quantity, i.CostPrice, i.TotalCost
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
            return BadRequest(new { message = "يوجد رصيد افتتاحي مسجل بالفعل لهذا العام. لا يمكن إضافة أكثر من رصيد افتتاحي واحد." });

        if (dto.Items == null || !dto.Items.Any())
            return BadRequest(new { message = "يجب إضافة صنف واحد على الأقل" });

        var refNo = await _seq.NextAsync("OB", async (db, pattern) =>
        {
            var max = await db.InventoryOpeningBalances
                .Where(x => EF.Functions.Like(x.Reference, pattern))
                .Select(x => x.Reference)
                .ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                      .DefaultIfEmpty(0).Max();
        });

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

            // 1. Update Inventory
            if (item.ProductId.HasValue)
            {
                await _inventory.LogMovementAsync(
                    InventoryMovementType.OpeningBalance, 
                    item.Quantity, 
                    item.ProductId, 
                    item.ProductVariantId, 
                    refNo, 
                    "ارصدة افتتاحية", 
                    userId,
                    item.CostPrice,
                    ob.CostCenter
                );

                // 2. Update Product Cost Price if requested
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
            sheet.Cell(1, 1).Value = "مرجع الرصيد:";
            sheet.Cell(1, 2).Value = ob.Reference;
            sheet.Cell(2, 1).Value = "التاريخ:";
            sheet.Cell(2, 2).Value = ob.Date.ToShortDateString();
            sheet.Cell(3, 1).Value = "الإجمالي:";
            sheet.Cell(3, 2).Value = ob.TotalValue;
            sheet.Cell(3, 2).Style.NumberFormat.Format = "#,##0.00";

            // Items Table
            var tableRange = sheet.Range(5, 1, 5, 6);
            sheet.Cell(5, 1).Value = "الصنف";
            sheet.Cell(5, 2).Value = "باركود / SKU";
            sheet.Cell(5, 3).Value = "المقاس/اللون";
            sheet.Cell(5, 4).Value = "الكمية";
            sheet.Cell(5, 5).Value = "التكلفة";
            sheet.Cell(5, 6).Value = "الإجمالي";
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

            sheet.Columns().AdjustToContents();

            using (var stream = new System.IO.MemoryStream())
            {
                workbook.SaveAs(stream);
                return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"OB-{ob.Reference}.xlsx");
            }
        }
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
            if (User.IsInRole("Admin")) 
            {
                _db.JournalEntries.Remove(journal); // الأدمن يحق له الحذف النهائي
            }
            else 
            {
                await _accounting.ReverseEntryAsync(journal.Id, $"إلغاء رصيد افتتاحي للمخزون - {ob.Reference}");
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
                    $"إلغاء رصيد افتتاحي - {ob.Reference}", 
                    userId,
                    item.CostPrice,
                    ob.CostCenter
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
        var existingSizes  = await _db.ProductVariants.Where(v => v.Size != null).Select(v => v.Size!).Distinct().ToListAsync();
        var existingColors = await _db.ProductVariants.Where(v => v.ColorAr != null).Select(v => v.ColorAr!).Distinct().ToListAsync();

        using var wb = new XLWorkbook();
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

        var ws = wb.Worksheets.Add("الأرصدة الافتتاحية");
        ws.RightToLeft = true;

        var headers = new[] { "الكود / SKU *", "الكمية *", "سعر التكلفة *", "المقاس", "اللون", "اسم الصنف (اختياري)" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Add Data Validation for 500 rows
        for (int r = 2; r <= 501; r++)
        {
            ws.Cell(r, 4).GetDataValidation().List("=SizesList");
            ws.Cell(r, 5).GetDataValidation().List("=ColorsList");
        }

        // Sample Data
        ws.Cell(2, 1).Value = "SKU-XYZ";
        ws.Cell(2, 2).Value = 10;
        ws.Cell(2, 3).Value = 150.00;
        ws.Cell(2, 4).Value = existingSizes.FirstOrDefault() ?? "XL";
        ws.Cell(2, 5).Value = existingColors.FirstOrDefault() ?? "Black";
        ws.Cell(2, 6).Value = "اسم توضيحي للمنتج";
        ws.Row(2).Style.Font.FontColor = XLColor.Gray;

        ws.Columns().AdjustToContents();
        ws.Column(1).Width = 20;
        ws.Column(6).Width = 30;

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "InventoryOpeningBalance_Template.xlsx");
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "لم يتم رفع ملف" });

        var items = new List<dynamic>();
        var errors = new List<string>();

        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            var products = await _db.Products.Include(p => p.Variants).Include(p => p.Images).ToListAsync();
            
            var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var firstRow = ws.Row(1);
            string Normalize(string s) => new string(s.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToLower();

            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                var hRaw = firstRow.Cell(c).GetString().Trim();
                if (string.IsNullOrEmpty(hRaw)) continue;
                headers[Normalize(hRaw)] = c;
            }

            int GetCol(params string[] aliases) {
                foreach (var a in aliases) {
                    if (headers.TryGetValue(Normalize(a), out var idx)) return idx;
                }
                return -1;
            }

            int colSku   = GetCol("الكود", "SKU", "Code");
            int colQty   = GetCol("الكمية", "Quantity", "Qty");
            int colCost  = GetCol("التكلفة", "Cost", "Price", "سعر التكلفة");
            int colSize  = GetCol("المقاس", "Size");
            int colColor = GetCol("اللون", "Color");

            if (colSku == -1 || colQty == -1 || colCost == -1)
                return BadRequest(new { message = "الأعمدة الإلزامية (الكود، الكمية، التكلفة) غير موجودة في الملف." });

            for (int r = 2; r <= lastRow; r++)
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
                    errors.Add($"سطر {r}: الكمية غير صحيحة للكود '{sku}'");
                    continue;
                }

                if (!decimal.TryParse(costStr, out var cost) || cost < 0)
                {
                    errors.Add($"سطر {r}: التكلفة غير صحيحة للكود '{sku}'");
                    continue;
                }

                var product = products.FirstOrDefault(p => 
                    string.Equals(p.SKU?.Trim(), sku, StringComparison.OrdinalIgnoreCase));

                if (product == null)
                {
                    errors.Add($"سطر {r}: الكود '{sku}' غير موجود في النظام");
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
                        errors.Add($"سطر {r}: الصنف '{sku}' له مقاسات، ولكن لم يتم العثور على المقاس '{size}' واللون '{color}' المدخلين");
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
                var errorWs = errorWb.Worksheets.Add("أخطاء الاستيراد");
                errorWs.RightToLeft = true;
                errorWs.Cell(1, 1).Value = "رقم السطر";
                errorWs.Cell(1, 2).Value = "وصف الخطأ";
                errorWs.Range(1, 1, 1, 2).Style.Font.Bold = true;
                errorWs.Range(1, 1, 1, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
                errorWs.Range(1, 1, 1, 2).Style.Font.FontColor = XLColor.White;

                for (int i = 0; i < errors.Count; i++)
                {
                    errorWs.Cell(i + 2, 1).Value = $"سطر {i+2}";
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
            return BadRequest(new { message = $"خطأ في قراءة ملف الإكسل: {ex.Message}" });
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
            Description: $"قيد إثبات أرصدة افتتاحية للمخزون - {ob.Reference}",
            Lines:       new List<CreateJournalLineDto>
            {
                new (inventoryId, ob.TotalValue, 0, "إثبات قيمة المخزون الافتتاحي"),
                new (openingId,   0, ob.TotalValue, "مقابل حساب الأرصدة الافتتاحية")
            },
            Type:        JournalEntryType.OpeningBalance,
            CostCenter:  ob.CostCenter
        );

        await _accounting.PostManualEntryAsync(dto, User);
    }
}
