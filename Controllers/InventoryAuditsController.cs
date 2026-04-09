using Sportive.API.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using System.Security.Claims;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class InventoryAuditsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    public InventoryAuditsController(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.InventoryAudits.Include(a => a.Items).AsQueryable();
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(a => a.AuditDate)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(a => new InventoryAuditSummaryDto(
                a.Id, a.Title, a.AuditDate, a.Status.ToString(),
                a.TotalExpectedValue, a.TotalActualValue, a.ValueDifference,
                a.Items.Count
            )).ToListAsync();

        return Ok(new PaginatedResult<InventoryAuditSummaryDto>(items, total, page, pageSize, (int)Math.Ceiling(total/(double)pageSize)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var a = await _db.InventoryAudits
            .Include(a => a.Items).ThenInclude(i => i.Product)
            .Include(a => a.Items).ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (a == null) return NotFound();

        return Ok(new InventoryAuditDetailDto(
            a.Id, a.Title, a.AuditDate, a.Description, a.Status.ToString(),
            a.TotalExpectedValue, a.TotalActualValue, a.ValueDifference,
            a.Items.Select(i => new InventoryAuditItemDto(
                i.Id, i.ProductId, i.Product?.NameAr, i.Product?.SKU,
                i.ProductVariantId, i.ProductVariant?.Size ?? i.ProductVariant?.ColorAr,
                i.ExpectedQuantity, i.ActualQuantity, i.Difference, i.UnitCost,
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
            Title = dto.Title,
            Description = dto.Description,
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Status = InventoryAuditStatus.Draft,
            AuditDate = TimeHelper.GetEgyptTime()
        };

        decimal totalExpected = 0;
        decimal totalActual = 0;

        foreach (var item in dto.Items)
        {
            decimal unitCost = 0;
            int currentStock = 0;

            if (item.ProductVariantId.HasValue)
            {
                var variant = await _db.ProductVariants.Include(v => v.Product).FirstOrDefaultAsync(v => v.Id == item.ProductVariantId);
                if (variant != null)
                {
                    unitCost     = variant.Product.CostPrice ?? 0;
                    currentStock = variant.StockQuantity;
                }
            }
            else if (item.ProductId.HasValue)
            {
                var product = await _db.Products.FindAsync(item.ProductId);
                if (product != null)
                {
                    unitCost     = product.CostPrice ?? 0;
                    currentStock = product.TotalStock;
                }
            }

            audit.Items.Add(new InventoryAuditItem
            {
                ProductId        = item.ProductId,
                ProductVariantId = item.ProductVariantId,
                ExpectedQuantity = currentStock,
                ActualQuantity   = item.ActualQuantity,
                UnitCost         = unitCost,
                Note             = item.Note
            });

            totalExpected += currentStock * unitCost;
            totalActual   += item.ActualQuantity * unitCost;
        }

        audit.TotalExpectedValue = totalExpected;
        audit.TotalActualValue   = totalActual;

        _db.InventoryAudits.Add(audit);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = audit.Id }, audit);
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

        await _db.SaveChangesAsync();
        return Ok(new { message = "تم اعتماد الجرد وتعديل المخزون بنجاح" });
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
