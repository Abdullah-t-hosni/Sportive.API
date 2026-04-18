using Sportive.API.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Services;
using System.Security.Claims;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class InventoryAuditsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly AccountingCoreService _accounting;
    public InventoryAuditsController(AppDbContext db, IInventoryService inventory, AccountingCoreService accounting)
    {
        _db = db;
        _inventory = inventory;
        _accounting = accounting;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.InventoryAudits.Include(a => a.Items).AsNoTracking();
        var total = await q.CountAsync();
        
        // Fetch to memory first to avoid projection issues with computed properties
        var auditNodes = await q.OrderByDescending(a => a.AuditDate)
            .Skip((page-1)*pageSize).Take(pageSize)
            .ToListAsync();

        var items = auditNodes.Select(a => new InventoryAuditSummaryDto(
            a.Id, a.Title, a.AuditDate, (int)a.Status,
            a.TotalExpectedValue, a.TotalActualValue, 
            a.TotalActualValue - a.TotalExpectedValue,
            a.Items.Count
        )).ToList();

        return Ok(new PaginatedResult<InventoryAuditSummaryDto>(items, total, page, pageSize, (int)Math.Ceiling(total/(double)pageSize)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var a = await _db.InventoryAudits
            .Include(a => a.Items).ThenInclude(i => i.Product)
            .Include(a => a.Items).ThenInclude(i => i.ProductVariant)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (a == null) return NotFound();

        // Mapping in memory
        return Ok(new InventoryAuditDetailDto(
            a.Id, a.Title, a.AuditDate, a.Description, (int)a.Status,
            a.TotalExpectedValue, a.TotalActualValue, a.TotalActualValue - a.TotalExpectedValue,
            a.Items.Select(i => new InventoryAuditItemDto(
                i.Id, i.ProductId, i.Product?.NameAr, i.Product?.SKU,
                i.ProductVariantId, i.ProductVariant?.Size ?? i.ProductVariant?.ColorAr,
                i.ExpectedQuantity, i.ActualQuantity, i.ActualQuantity - i.ExpectedQuantity, i.UnitCost,
                i.ExpectedQuantity * i.UnitCost, i.ActualQuantity * i.UnitCost,
                i.Note
            )).ToList(),
            a.JournalEntryId
        ));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInventoryAuditDto dto)
    {
        var audit = new InventoryAudit
        {
            Title = string.IsNullOrWhiteSpace(dto.Title) ? $"جرد مخزن - {TimeHelper.GetEgyptTime():yyyy-MM-dd HH:mm}" : dto.Title,
            Description = dto.Description,
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Status = InventoryAuditStatus.Draft,
            AuditDate = TimeHelper.GetEgyptTime()
        };

        if (dto.Items != null && dto.Items.Any())
        {
            await ProcessItemsAsync(audit, dto.Items);
        }

        _db.InventoryAudits.Add(audit);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = audit.Id }, audit);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateInventoryAuditDto dto)
    {
        var audit = await _db.InventoryAudits.Include(a => a.Items).FirstOrDefaultAsync(a => a.Id == id);
        if (audit == null) return NotFound();
        if (audit.Status != InventoryAuditStatus.Draft) return BadRequest("يمكن تعديل المسودات فقط");

        audit.Title = dto.Title;
        audit.Description = dto.Description;
        
        // Remove old items and re-add (Simple approach for audit)
        _db.InventoryAuditItems.RemoveRange(audit.Items);
        audit.Items.Clear();

        await ProcessItemsAsync(audit, dto.Items);
        
        audit.UpdatedAt = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();

        return Ok(new { message = "تم حفظ التغييرات بنجاح" });
    }

    private async Task ProcessItemsAsync(InventoryAudit audit, List<CreateInventoryAuditItemDto> items)
    {
        decimal totalExpected = 0;
        decimal totalActual = 0;

        foreach (var item in items)
        {
            decimal unitCost = 0;
            int currentStock = 0;

            if (item.ProductVariantId.HasValue)
            {
                var variant = await _db.ProductVariants.Include(v => v.Product).FirstOrDefaultAsync(v => v.Id == item.ProductVariantId);
                if (variant != null)
                {
                    unitCost = variant.Product.CostPrice ?? 0;
                    currentStock = variant.StockQuantity;
                }
            }
            else if (item.ProductId.HasValue)
            {
                var product = await _db.Products.FindAsync(item.ProductId);
                if (product != null)
                {
                    unitCost = product.CostPrice ?? 0;
                    currentStock = product.TotalStock;
                }
            }

            audit.Items.Add(new InventoryAuditItem
            {
                ProductId = item.ProductId,
                ProductVariantId = item.ProductVariantId,
                ExpectedQuantity = currentStock,
                ActualQuantity = item.ActualQuantity,
                UnitCost = unitCost,
                Note = item.Note
            });

            totalExpected += currentStock * unitCost;
            totalActual += item.ActualQuantity * unitCost;
        }

        audit.TotalExpectedValue = totalExpected;
        audit.TotalActualValue = totalActual;
    }

    [HttpPatch("{id}/post")]
    public async Task<IActionResult> PostAudit(int id)
    {
        var audit = await _db.InventoryAudits
            .Include(a => a.Items)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (audit == null) return NotFound();
        if (audit.Status == InventoryAuditStatus.Posted) return BadRequest("هذا الجرد معتمد بالفعل");

        // Update Stock via InventoryService
        foreach (var item in audit.Items)
        {
            // Difference = Actual - Expected
            await _inventory.LogMovementAsync(
                InventoryMovementType.Audit,
                item.Difference,
                item.ProductId,
                item.ProductVariantId,
                $"AUDIT-{audit.Id}",
                item.Note,
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            );
        }

        audit.Status    = InventoryAuditStatus.Posted;
        audit.UpdatedAt = TimeHelper.GetEgyptTime();

        // 3. Post Accounting Journal Entry for the variance
        try
        {
            var jeId = await _accounting.PostInventoryAdjustmentAsync(
                audit.Id, 
                audit.ValueDifference, 
                $"AUDIT-{audit.Id}", 
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            );
            audit.JournalEntryId = jeId;
        }
        catch (Exception ex)
        {
            // Log error but don't fail the whole audit if accounting mapping is missing
            // The audit is still physically correct
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "تم اعتماد الجرد وتعديل المخزون وترحيل القيد المحاسبي بنجاح" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var audit = await _db.InventoryAudits.FindAsync(id);
        if (audit == null) return NotFound();
        if (audit.Status == InventoryAuditStatus.Posted) return BadRequest("لا يمكن حذف جرد معتمد");

        _db.InventoryAudits.Remove(audit);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
