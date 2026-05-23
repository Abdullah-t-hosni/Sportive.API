using Sportive.API.Attributes;
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
[RequirePermission(ModuleKeys.InventoryCount)]
public class InventoryAuditsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly AccountingCoreService _accounting;
    private readonly ILogger<InventoryAuditsController> _logger;
    private readonly ITranslator _t;
    private readonly IAccountingService _accountingService;

    public InventoryAuditsController(AppDbContext db, IInventoryService inventory, AccountingCoreService accounting, ILogger<InventoryAuditsController> logger, ITranslator t, IAccountingService accountingService)
    {
        _db = db;
        _inventory = inventory;
        _accounting = accounting;
        _logger = logger;
        _t = t;
        _accountingService = accountingService;
    }

    private async Task<bool> CheckPerms(string perm, bool edit = false)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return false;
        
        // Admins bypass
        if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin")) return true;

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
                    Title = a.Title ?? _t.Get("InventoryAudits.UntitledAudit"), 
                    a.AuditDate, 
                    StatusInt = (int)a.Status,
                    a.TotalExpectedValue, 
                    a.TotalActualValue,
                    ItemCount = a.Items.Count,
                    a.CostCenter
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
                a.ItemCount,
                a.CostCenter
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
                message = _t.Get("InventoryAudits.ErrorLoadingLogs"), 
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
                a.JournalEntryId,
                a.CostCenter
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetById InventoryAudit {Id}", id);
            return StatusCode(500, new { message = _t.Get("InventoryAudits.ErrorLoadingDetails"), detail = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInventoryAuditDto dto)
    {
        if (!await CheckPerms(ModuleKeys.InventoryCount, true)) return Forbid();
        var audit = new InventoryAudit
        {
            Title = string.IsNullOrWhiteSpace(dto.Title) ? _t.Get("InventoryAudits.DefaultTitle", TimeHelper.GetEgyptTime().ToString("yyyy-MM-dd HH:mm")) : dto.Title,
            Description = dto.Description,
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Status = InventoryAuditStatus.Draft,
            AuditDate = TimeHelper.GetEgyptTime(),
            CostCenter = dto.CostCenter
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

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var audit = await _db.InventoryAudits.Include(a => a.Items).FirstOrDefaultAsync(a => a.Id == id);
                if (audit == null) return NotFound();
                
                // If it was posted, we revert stock, delete movements, and reverse/delete the journal entry
                if (audit.Status == InventoryAuditStatus.Posted)
                {
                    _logger.LogInformation("Reverting Posted Audit {Id} to Draft for editing", id);
                    
                    // 1. Find all movements related to this audit
                    var refs = new List<string> { $"AUDIT-{audit.Id}", $"REVERT-AUDIT-{audit.Id}", $"DELETE-AUDIT-{audit.Id}" };
                    var movements = await _db.InventoryMovements
                        .Where(m => m.Reference != null && refs.Contains(m.Reference))
                        .ToListAsync();

                    // 2. Revert the stock level of each movement
                    foreach (var mv in movements)
                    {
                        if (mv.ProductVariantId.HasValue)
                        {
                            var variant = await _db.ProductVariants.Include(v => v.Product).FirstOrDefaultAsync(v => v.Id == mv.ProductVariantId.Value);
                            if (variant != null)
                            {
                                variant.StockQuantity -= mv.Quantity;
                                variant.Product.TotalStock -= mv.Quantity;
                                variant.UpdatedAt = TimeHelper.GetEgyptTime();
                                variant.Product.UpdatedAt = TimeHelper.GetEgyptTime();
                            }
                        }
                        else if (mv.ProductId.HasValue)
                        {
                            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == mv.ProductId.Value);
                            if (product != null)
                            {
                                product.TotalStock -= mv.Quantity;
                                product.UpdatedAt = TimeHelper.GetEgyptTime();
                            }
                        }
                    }

                    // 3. Delete the movements
                    _db.InventoryMovements.RemoveRange(movements);

                    // 4. Reverse or delete the journal entry
                    if (audit.JournalEntryId.HasValue)
                    {
                        var journal = await _db.JournalEntries.Include(j => j.Lines).FirstOrDefaultAsync(j => j.Id == audit.JournalEntryId.Value);
                        if (journal != null)
                        {
                            if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin"))
                            {
                                var childReversals = await _db.JournalEntries
                                    .Include(j => j.Lines)
                                    .Where(j => j.ReversalOfId == journal.Id)
                                    .ToListAsync();
                                if (childReversals.Any())
                                {
                                    foreach (var child in childReversals)
                                    {
                                        _db.JournalLines.RemoveRange(child.Lines);
                                    }
                                    _db.JournalEntries.RemoveRange(childReversals);
                                }
                                _db.JournalLines.RemoveRange(journal.Lines);
                                _db.JournalEntries.Remove(journal);
                            }
                            else
                            {
                                await _accountingService.ReverseEntryAsync(journal.Id, $"تعديل الجرد وتحويله لمسودة #{audit.Id}");
                            }
                        }
                    }

                    audit.Status = InventoryAuditStatus.Draft;
                    audit.CostCenter = dto.CostCenter;
                    audit.JournalEntryId = null;
                }

                audit.Title = dto.Title;
                audit.Description = dto.Description;
                audit.CostCenter = dto.CostCenter;
                
                // Remove old items and re-add (Simple approach for audit)
                _db.InventoryAuditItems.RemoveRange(audit.Items);
                audit.Items.Clear();

                await ProcessItemsAsync(audit, dto.Items);
                
                audit.UpdatedAt = TimeHelper.GetEgyptTime();
                await _db.SaveChangesAsync();

                await transaction.CommitAsync();
                return Ok(new { message = _t.Get("InventoryAudits.ChangesSaved") });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error updating inventory audit {Id}", id);
                throw;
            }
        });
    }

    private async Task ProcessItemsAsync(InventoryAudit audit, List<CreateInventoryAuditItemDto> items)
    {
        decimal totalExpected = 0;
        decimal totalActual = 0;

        var variantIds = items.Where(i => i.ProductVariantId.HasValue).Select(i => i.ProductVariantId!.Value).Distinct().ToList();
        var productIds = items.Where(i => i.ProductId.HasValue && !i.ProductVariantId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();

        var variants = variantIds.Any() 
            ? await _db.ProductVariants.Include(v => v.Product).Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id)
            : new Dictionary<int, ProductVariant>();

        var products = productIds.Any()
            ? await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id)
            : new Dictionary<int, Product>();

        foreach (var item in items)
        {
            decimal unitCost = 0;
            int currentStock = 0;

            if (item.ProductVariantId.HasValue && variants.TryGetValue(item.ProductVariantId.Value, out var v))
            {
                unitCost = v.Product?.CostPrice ?? 0;
                currentStock = v.StockQuantity;
            }
            else if (item.ProductId.HasValue && products.TryGetValue(item.ProductId.Value, out var p))
            {
                unitCost = p.CostPrice ?? 0;
                currentStock = p.TotalStock;
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

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var audit = await _db.InventoryAudits
                    .Include(a => a.Items)
                    .FirstOrDefaultAsync(a => a.Id == id);

                if (audit == null) return NotFound();
                if (audit.Status == InventoryAuditStatus.Posted) return BadRequest(_t.Get("InventoryAudits.AlreadyPosted"));

                // Update Stock via InventoryService
                foreach (var item in audit.Items)
                {
                    // Difference = Actual - Expected
                    await _inventory.LogMovementAsync(
                        type: InventoryMovementType.Audit,
                        quantity: item.Difference,
                        productId: item.ProductId,
                        variantId: item.ProductVariantId,
                        reference: $"AUDIT-{audit.Id}",
                        note: item.Note,
                        userId: User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                        unitCost: item.UnitCost,
                        costCenter: audit.CostCenter,
                        force: true,           // Audit overrides stock — force the physical count
                        ignoreIdempotency: true
                    );
                }

                audit.Status    = InventoryAuditStatus.Posted;
                audit.UpdatedAt = TimeHelper.GetEgyptTime();

                // 3. Post Accounting Journal Entry for the variance
                var jeId = await _accounting.PostInventoryAdjustmentAsync(
                    auditId: audit.Id, 
                    netImpact: audit.ValueDifference, 
                    reference: $"AUDIT-{audit.Id}", 
                    userId: User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    costCenter: audit.CostCenter
                );
                audit.JournalEntryId = jeId;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { message = _t.Get("InventoryAudits.AuditApprovedSuccess") });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error posting inventory audit {Id}", id);
                throw;
            }
        });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await CheckPerms(ModuleKeys.InventoryCount, true)) return Forbid();

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var audit = await _db.InventoryAudits.Include(a => a.Items).FirstOrDefaultAsync(a => a.Id == id);
                if (audit == null) return NotFound();

                if (audit.Status == InventoryAuditStatus.Posted)
                {
                    // 1. Find all movements related to this audit
                    var refs = new List<string> { $"AUDIT-{audit.Id}", $"REVERT-AUDIT-{audit.Id}", $"DELETE-AUDIT-{audit.Id}" };
                    var movements = await _db.InventoryMovements
                        .Where(m => m.Reference != null && refs.Contains(m.Reference))
                        .ToListAsync();

                    // 2. Revert the stock level of each movement
                    foreach (var mv in movements)
                    {
                        if (mv.ProductVariantId.HasValue)
                        {
                            var variant = await _db.ProductVariants.Include(v => v.Product).FirstOrDefaultAsync(v => v.Id == mv.ProductVariantId.Value);
                            if (variant != null)
                            {
                                variant.StockQuantity -= mv.Quantity;
                                variant.Product.TotalStock -= mv.Quantity;
                                variant.UpdatedAt = TimeHelper.GetEgyptTime();
                                variant.Product.UpdatedAt = TimeHelper.GetEgyptTime();
                            }
                        }
                        else if (mv.ProductId.HasValue)
                        {
                            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == mv.ProductId.Value);
                            if (product != null)
                            {
                                product.TotalStock -= mv.Quantity;
                                product.UpdatedAt = TimeHelper.GetEgyptTime();
                            }
                        }
                    }

                    // 3. Delete the movements
                    _db.InventoryMovements.RemoveRange(movements);

                    // 4. Reverse or delete the journal entry
                    if (audit.JournalEntryId.HasValue)
                    {
                        var journal = await _db.JournalEntries.Include(j => j.Lines).FirstOrDefaultAsync(j => j.Id == audit.JournalEntryId.Value);
                        if (journal != null)
                        {
                            if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin"))
                            {
                                var childReversals = await _db.JournalEntries
                                    .Include(j => j.Lines)
                                    .Where(j => j.ReversalOfId == journal.Id)
                                    .ToListAsync();
                                if (childReversals.Any())
                                {
                                    foreach (var child in childReversals)
                                    {
                                        _db.JournalLines.RemoveRange(child.Lines);
                                    }
                                    _db.JournalEntries.RemoveRange(childReversals);
                                }
                                _db.JournalLines.RemoveRange(journal.Lines);
                                _db.JournalEntries.Remove(journal);
                            }
                            else
                            {
                                await _accountingService.ReverseEntryAsync(journal.Id, $"حذف الجرد #{audit.Id}");
                            }
                        }
                    }
                }

                _db.InventoryAudits.Remove(audit);
                await _db.SaveChangesAsync();

                await transaction.CommitAsync();
                return NoContent();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error deleting inventory audit {Id}", id);
                throw;
            }
        });
    }
}

