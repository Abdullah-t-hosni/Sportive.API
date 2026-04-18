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

    public InventoryOpeningBalanceController(
        AppDbContext db, 
        SequenceService seq, 
        IAccountingService accounting,
        IInventoryService inventory,
        AccountingCoreService core)
    {
        _db = db;
        _seq = seq;
        _accounting = accounting;
        _inventory = inventory;
        _core = core;
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
                x.Id, x.Reference, x.Date, x.TotalValue, x.Items.Count
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
            )).ToList()
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
                    item.CostPrice
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
                    item.CostPrice
                );
            }
        }

        // 2. Void/Delete Journal Entry - Handled above in pro-accounting block

        // 3. Remove Opening Balance record
        _db.InventoryOpeningBalances.Remove(ob);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private async Task PostJournalAsync(InventoryOpeningBalance ob)
    {
        // Get Mapped Accounts - STRICT CHECK
        var (inventoryId, exactInv, invError) = await _core.GetAccountIdAsync(MappingKeys.Inventory);
        var (openingId, exactOpe, opeError)   = await _core.GetAccountIdAsync(MappingKeys.OpeningEquity);

        if (!exactInv || inventoryId == 0)
            throw new InvalidOperationException($"فشل ترحيل القيد: حساب المخزون غير مربوط بشكل صحيح في الإعدادات. ({invError})");

        if (!exactOpe || openingId == 0)
            throw new InvalidOperationException($"فشل ترحيل القيد: حساب الأرصدة الافتتاحية غير مربوط بشكل صحيح في الإعدادات. ({opeError})");

        var dto = new CreateJournalEntryDto(
            EntryDate:   ob.Date,
            Reference:   ob.Reference,
            Description: $"قيد إثبات أرصدة افتتاحية للمخزون - {ob.Reference}",
            Lines:       new List<CreateJournalLineDto>
            {
                new (inventoryId, ob.TotalValue, 0, "إثبات قيمة المخزون الافتتاحي"),
                new (openingId,   0, ob.TotalValue, "مقابل حساب الأرصدة الافتتاحية")
            },
            Type:        JournalEntryType.OpeningBalance
        );

        await _accounting.PostManualEntryAsync(dto, User);
    }
}
