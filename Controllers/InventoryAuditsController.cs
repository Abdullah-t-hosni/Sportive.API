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
[Authorize(Roles = "Admin,Manager,Accountant,Staff,Cashier")]
public class InventoryAuditsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly AccountingCoreService _accounting;
    private readonly ILogger<InventoryAuditsController> _logger;

    public InventoryAuditsController(AppDbContext db, IInventoryService inventory, AccountingCoreService accounting, ILogger<InventoryAuditsController> logger)
    {
        _db = db;
        _inventory = inventory;
        _accounting = accounting;
        _logger = logger;
    }

    private async Task<bool> CheckPerms(string perm, bool edit = false)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return false;
        
        // Admins bypass
        if (User.IsInRole("Admin")) return true;

        var userPerm = await _db.UserModulePermissions
            .FirstOrDefaultAsync(p => p.UserAccountID == userId && p.ModuleKey == perm);
        
        if (userPerm == null) return false;
        return edit ? userPerm.CanEdit : userPerm.CanView;
    }

    [HttpGet("ping")]
    [AllowAnonymous]
    public IActionResult Ping() => Ok("Audit Controller is Alive");

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try 
        {
            _logger.LogInformation("InventoryAudits: GetAll called. Page={Page}, PageSize={PageSize}", page, pageSize);

            if (!await CheckPerms(ModuleKeys.InventoryCount)) 
            {
                _logger.LogWarning("InventoryAudits: Permission denied for user.");
                return Forbid();
            }

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50; // Cap at 50

            var itemsQuery = _db.InventoryAudits.AsNoTracking();
            
            _logger.LogInformation("InventoryAudits: Counting records...");
            var total = await itemsQuery.CountAsync();
            
            _logger.LogInformation("InventoryAudits: Fetching items (Skip={Skip}, Take={Take})...", (page - 1) * pageSize, pageSize);
            
            var items = await itemsQuery.OrderByDescending(a => a.AuditDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new {
                    a.Id, 
                    Title = a.Title ?? "جرد بدون عنوان", 
                    a.AuditDate, 
                    StatusInt = (int)a.Status,
                    a.TotalExpectedValue, 
                    a.TotalActualValue,
                    ItemCount = a.Items.Count
                })
                .ToListAsync();

            _logger.LogInformation("Fetched {Count} items for InventoryAudits", items.Count);

            var result = items.Select(a => new InventoryAuditSummaryDto(
                a.Id, 
                a.Title, 
                a.AuditDate, 
                a.StatusInt,
                a.TotalExpectedValue, 
                a.TotalActualValue, 
                a.TotalActualValue - a.TotalExpectedValue,
                a.ItemCount
            )).ToList();

            var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
            return Ok(new PaginatedResult<InventoryAuditSummaryDto>(result, total, page, pageSize, totalPages));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetAll InventoryAudits: {Message}", ex.Message);
            var fullMsg = ex.InnerException != null ? $"{ex.Message} -> {ex.InnerException.Message}" : ex.Message;
            return StatusCode(500, new { 
                success = false, 
                message = "خطأ في تحميل سجلات الجرد", 
                detail = fullMsg,
                type = ex.GetType().Name,
                stack = ex.StackTrace 
            });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (!await CheckPerms(ModuleKeys.InventoryCount)) return Forbid();

        try
        {
            var a = await _db.InventoryAudits
                .Include(a => a.Items).ThenInclude(i => i.Product)
                .Include(a => a.Items).ThenInclude(i => i.ProductVariant)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (a == null) return NotFound();

            return Ok(new InventoryAuditDetailDto(
                a.Id, a.Title, a.AuditDate, a.Description, (int)a.Status,
                a.TotalExpectedValue, a.TotalActualValue, a.ValueDifference,
                a.Items.Select(i => {
                    var variantName = i.ProductVariant != null ? $"{i.ProductVariant.Size} {i.ProductVariant.ColorAr}".Trim() : null;
                    var imageUrl = i.ProductVariant?.ImageUrl ?? i.Product?.Images?.FirstOrDefault(img => img.IsMain)?.ImageUrl ?? i.Product?.Images?.FirstOrDefault()?.ImageUrl;
                    
                    return new InventoryAuditItemDto(
                        i.Id, i.ProductId, i.Product?.NameAr, i.Product?.SKU,
                        i.ProductVariantId, variantName,
                        i.ExpectedQuantity, i.ActualQuantity, i.Difference, i.UnitCost,
                        i.ExpectedQuantity * i.UnitCost, i.ActualQuantity * i.UnitCost,
                        i.Note, imageUrl
                    );
                }).ToList(),
                a.JournalEntryId
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetById InventoryAudit {Id}", id);
            return StatusCode(500, new { message = "خطأ في تحميل تفاصيل الجرد", detail = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInventoryAuditDto dto)
    {
        if (!await CheckPerms(ModuleKeys.InventoryCount, true)) return Forbid();
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
        if (!await CheckPerms(ModuleKeys.InventoryCount, true)) return Forbid();
        var audit = await _db.InventoryAudits.Include(a => a.Items).FirstOrDefaultAsync(a => a.Id == id);
        if (audit == null) return NotFound();
        
        // If it was posted, we might want to log that we're reverting it
        if (audit.Status == InventoryAuditStatus.Posted)
        {
            _logger.LogInformation("Reverting Posted Audit {Id} to Draft for editing", id);
            // Optional: You could reverse stock movements here, 
            // but for simplicity we'll just let the next 'Post' recalculate everything.
            // However, to keep stock accurate, we should probably reverse the LAST movements.
            foreach (var item in audit.Items)
            {
                await _inventory.LogMovementAsync(
                    InventoryMovementType.Adjustment, 
                    -item.Difference, // Reverse the previous audit impact
                    item.ProductId, 
                    item.ProductVariantId, 
                    $"REVERT-AUDIT-{audit.Id}", 
                    "Reverting for edit", 
                    User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                );
            }
            audit.Status = InventoryAuditStatus.Draft;
            audit.JournalEntryId = null; // Entry should be reversed or ignored
        }

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
        if (!await CheckPerms(ModuleKeys.InventoryCount, true)) return Forbid();
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
        var jeId = await _accounting.PostInventoryAdjustmentAsync(
            auditId: audit.Id, 
            netImpact: audit.ValueDifference, 
            reference: $"AUDIT-{audit.Id}", 
            userId: User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        );
        audit.JournalEntryId = jeId;

        await _db.SaveChangesAsync();
        return Ok(new { message = "تم اعتماد الجرد وتعديل المخزون وترحيل القيد المحاسبي بنجاح" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await CheckPerms(ModuleKeys.InventoryCount, true)) return Forbid();
        var audit = await _db.InventoryAudits.Include(a => a.Items).FirstOrDefaultAsync(a => a.Id == id);
        if (audit == null) return NotFound();

        if (audit.Status == InventoryAuditStatus.Posted)
        {
            // Reverse stock movements
            foreach (var item in audit.Items)
            {
                await _inventory.LogMovementAsync(
                    InventoryMovementType.Adjustment,
                    -item.Difference,
                    item.ProductId,
                    item.ProductVariantId,
                    $"DELETE-AUDIT-{audit.Id}",
                    "Audit Deleted",
                    User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                );
            }
        }

        _db.InventoryAudits.Remove(audit);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
