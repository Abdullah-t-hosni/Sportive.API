using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;
using System.Security.Claims;

namespace Sportive.API.Controllers;

/// <summary>
/// Inventory Intelligence Controller â€” Phase 2 Enhancements
/// ÙŠØ­ØªÙˆÙŠ Ø¹Ù„Ù‰: Ø¬Ø¯ÙˆÙ„ Ø§Ø³ØªØ­Ù‚Ø§Ù‚Ø§Øª Ø§Ù„Ù…ÙˆØ±Ø¯ÙŠÙ†ØŒ ØªÙ†Ø¨ÙŠÙ‡Ø§Øª Ø§Ù„Ù…Ù‚Ø§Ø³Ø§ØªØŒ Ø§Ù„Ø¬Ø±Ø¯ Ø§Ù„Ø¬Ø²Ø¦ÙŠ Ø§Ù„ÙŠÙˆÙ…ÙŠ
/// </summary>
[ApiController]
[Route("api/operationalreports")]
[RequirePermission(ModuleKeys.ReportsMain)]
public class InventoryIntelligenceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccountingCoreService _accounting;
    public InventoryIntelligenceController(AppDbContext db, AccountingCoreService accounting)
    {
        _db = db;
        _accounting = accounting;
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // 13. Ø¬Ø¯ÙˆÙ„ Ø§Ø³ØªØ­Ù‚Ø§Ù‚Ø§Øª Ø§Ù„Ù…ÙˆØ±Ø¯ÙŠÙ† Ø§Ù„Ø£Ø³Ø¨ÙˆØ¹ÙŠ (Payables Weekly Schedule)
    // GET /api/operationalreports/payables-schedule?weeks=4
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet("payables-schedule")]
    public async Task<IActionResult> PayablesSchedule([FromQuery] int weeks = 4)
    {
        var today = TimeHelper.GetEgyptTime().Date;

        var invoices = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Where(i => i.Status != PurchaseInvoiceStatus.Cancelled
                     && i.Status != PurchaseInvoiceStatus.Returned
                     && (i.TotalAmount - i.PaidAmount - i.ReturnedAmount) > 0)
            .ToListAsync();

        var buckets = new List<object>();
        decimal grandTotal = 0;

        // â”€â”€ Ø³Ø·Ù„ Ø§Ù„Ù…ØªØ£Ø®Ø±Ø§Øª (Overdue) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var overdueList = invoices
            .Where(i => i.DueDate.HasValue && i.DueDate.Value.Date < today)
            .ToList();
        var overdueTot = overdueList.Sum(i => i.RemainingAmount);
        grandTotal += overdueTot;

        buckets.Add(new
        {
            week      = 0,
            label     = "Ù…ØªØ£Ø®Ø±Ø© (Overdue)",
            weekStart = (string?)null,
            weekEnd   = (string?)null,
            isOverdue = true,
            count     = overdueList.Count,
            totalDue  = overdueTot,
            invoices  = overdueList.Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                SupplierName  = i.Supplier.Name,
                SupplierPhone = i.Supplier.Phone,
                DueDate       = i.DueDate!.Value.ToString("yyyy-MM-dd"),
                i.RemainingAmount,
                i.TotalAmount,
                DaysOverdue   = (int)(today - i.DueDate!.Value.Date).TotalDays
            }).OrderByDescending(x => x.DaysOverdue).ToList<object>()
        });

        // â”€â”€ Ø£Ø³Ø§Ø¨ÙŠØ¹ Ù…Ø³ØªÙ‚Ø¨Ù„ÙŠØ© â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        for (int w = 1; w <= weeks; w++)
        {
            var wStart = today.AddDays((w - 1) * 7);
            var wEnd   = today.AddDays(w * 7 - 1);

            var wList = invoices
                .Where(i => i.DueDate.HasValue
                         && i.DueDate.Value.Date >= wStart
                         && i.DueDate.Value.Date <= wEnd)
                .ToList();
            var wTot = wList.Sum(i => i.RemainingAmount);
            grandTotal += wTot;

            buckets.Add(new
            {
                week      = w,
                label     = $"Ø£Ø³Ø¨ÙˆØ¹ {w} ({wStart:MM/dd} - {wEnd:MM/dd})",
                weekStart = wStart.ToString("yyyy-MM-dd"),
                weekEnd   = wEnd.ToString("yyyy-MM-dd"),
                isOverdue = false,
                count     = wList.Count,
                totalDue  = wTot,
                invoices  = wList.Select(i => new
                {
                    i.Id,
                    i.InvoiceNumber,
                    SupplierName  = i.Supplier.Name,
                    SupplierPhone = i.Supplier.Phone,
                    DueDate       = i.DueDate!.Value.ToString("yyyy-MM-dd"),
                    i.RemainingAmount,
                    i.TotalAmount,
                    DaysUntilDue  = (int)(i.DueDate!.Value.Date - today).TotalDays
                }).OrderBy(x => x.DaysUntilDue).ToList<object>()
            });
        }

        // â”€â”€ ÙÙˆØ§ØªÙŠØ± Ø¢Ø¬Ù„Ø© Ø¨Ø¯ÙˆÙ† ØªØ§Ø±ÙŠØ® Ø§Ø³ØªØ­Ù‚Ø§Ù‚ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var undated       = invoices.Where(i => !i.DueDate.HasValue && i.PaymentTerms == PaymentTerms.Credit).ToList();
        var undatedAmount = undated.Sum(i => i.RemainingAmount);

        return Ok(new
        {
            today         = today.ToString("yyyy-MM-dd"),
            weeks,
            grandTotal,
            totalOverdue  = overdueTot,
            buckets,
            undatedCount  = undated.Count,
            undatedAmount
        });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // 14. ØªÙ†Ø¨ÙŠÙ‡Ø§Øª Ù†Ù‚Øµ Ø§Ù„Ù…Ø®Ø²ÙˆÙ† Ø¹Ù„Ù‰ Ù…Ø³ØªÙˆÙ‰ Ø§Ù„Ù…Ù‚Ø§Ø³/Ø§Ù„Ù„ÙˆÙ†
    // GET /api/operationalreports/variant-reorder-alerts?threshold=2
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet("variant-reorder-alerts")]
    [HttpGet("variant-reorder")] // Alias for varying client paths
    public async Task<IActionResult> VariantReorderAlerts(
        [FromQuery] int   threshold  = 2,
        [FromQuery] int?  categoryId = null,
        [FromQuery] int?  brandId    = null,
        [FromQuery] bool  zeroOnly   = false)
    {
        var q = _db.ProductVariants
            .Include(v => v.Product).ThenInclude(p => p!.Category)
            .Where(v => v.Product != null
                     && (v.Product.Status == ProductStatus.Active
                      || v.Product.Status == ProductStatus.OutOfStock));

        if (zeroOnly)
            q = q.Where(v => v.StockQuantity <= 0);
        else
            q = q.Where(v => v.StockQuantity <= (v.ReorderLevel > 0 ? v.ReorderLevel : threshold));

        if (categoryId.HasValue && categoryId > 0)
        {
            var catIds = await FilterHelper.GetCategoryFamilyIds(_db, categoryId);
            q = q.Where(v => v.Product!.CategoryId.HasValue && catIds.Contains(v.Product.CategoryId.Value));
        }

        if (brandId.HasValue && brandId > 0)
        {
            var bIds = await FilterHelper.GetBrandFamilyIds(_db, brandId);
            q = q.Where(v => v.Product!.BrandId.HasValue && bIds.Contains(v.Product.BrandId.Value));
        }

        var variants = await q
            .OrderBy(v => v.StockQuantity)
            .ThenBy(v => v.Product!.NameAr)
            .Select(v => new
            {
                ProductId    = v.ProductId,
                ProductName  = v.Product!.NameAr,
                ProductSKU   = v.Product!.SKU,
                CategoryName = v.Product!.Category != null ? v.Product.Category.NameAr : "",
                VariantId    = v.Id,
                Size         = v.Size,
                Color        = v.ColorAr ?? v.Color,
                Stock        = v.StockQuantity,
                ReorderLevel = v.ReorderLevel > 0 ? v.ReorderLevel : threshold,
                Shortage     = Math.Max(0, (v.ReorderLevel > 0 ? v.ReorderLevel : threshold) - v.StockQuantity),
                IsZero       = v.StockQuantity <= 0,
                IsCritical   = v.StockQuantity == 1,
                CostValue    = (decimal)v.StockQuantity * (v.Product!.CostPrice ?? 0)
            })
            .ToListAsync();

        return Ok(new
        {
            threshold,
            totalAlerts    = variants.Count,
            zeroStockCount = variants.Count(v => v.IsZero),
            criticalCount  = variants.Count(v => v.IsCritical),
            rows           = variants
        });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // 15. Ø§Ù„Ø¬Ø±Ø¯ Ø§Ù„Ø¬Ø²Ø¦ÙŠ Ø§Ù„ÙŠÙˆÙ…ÙŠ (Daily Cycle Count)
    // GET  /api/operationalreports/cycle-count-today?count=5
    // POST /api/operationalreports/cycle-count-submit
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet("cycle-count-today")]
    public async Task<IActionResult> CycleCountToday(
        [FromQuery] int     count      = 5,
        [FromQuery] string? search     = null,
        [FromQuery] int?    categoryId = null,
        [FromQuery] int?    brandId    = null,
        [FromQuery] string? color      = null,
        [FromQuery] string? size       = null)
    {
        var today = TimeHelper.GetEgyptTime().Date;

        // Ø§Ù„Ù€ variants Ø§Ù„ØªÙŠ Ø¬ÙØ±Ø¯Øª Ø§Ù„ÙŠÙˆÙ… Ø¨Ø§Ù„ÙØ¹Ù„ Ø¨Ø§Ù„Ø¬Ø±Ø¯ Ø§Ù„Ø¬Ø²Ø¦ÙŠ
        var auditedToday = await _db.InventoryMovements
            .Where(m => m.Type == InventoryMovementType.Audit
                     && m.CreatedAt >= today
                     && m.Reference != null
                     && m.Reference.StartsWith("CYCLE-"))
            .Select(m => m.ProductVariantId)
            .Distinct()
            .ToListAsync();

        var q = _db.ProductVariants
            .Include(v => v.Product)
            .Where(v => v.Product != null
                     && v.Product.Status == ProductStatus.Active
                     && !auditedToday.Contains(v.Id));

        // ØªØ·Ø¨ÙŠÙ‚ Ø§Ù„ÙÙ„Ø§ØªØ± Ø¥Ø°Ø§ ÙˆÙØ¬Ø¯Øª
        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(v => v.Product!.NameAr.Contains(search) || v.Product.SKU.Contains(search));
        }
        if (categoryId.HasValue && categoryId > 0)
        {
            var catIds = await FilterHelper.GetCategoryFamilyIds(_db, categoryId);
            q = q.Where(v => v.Product!.CategoryId.HasValue && catIds.Contains(v.Product.CategoryId.Value));
        }
        if (brandId.HasValue && brandId > 0)
        {
            var bIds = await FilterHelper.GetBrandFamilyIds(_db, brandId);
            q = q.Where(v => v.Product!.BrandId.HasValue && bIds.Contains(v.Product.BrandId.Value));
        }
        if (!string.IsNullOrEmpty(color))
        {
            q = q.Where(v => v.Color == color || v.ColorAr == color);
        }
        if (!string.IsNullOrEmpty(size))
        {
            q = q.Where(v => v.Size == size);
        }

        var allVariants = await q.Select(v => new
        {
            VariantId   = v.Id,
            ProductId   = v.ProductId,
            ProductName = v.Product!.NameAr,
            SKU         = v.Product!.SKU,
            Size        = v.Size,
            Color       = v.ColorAr ?? v.Color,
            SystemStock = v.StockQuantity,
            CostPrice   = v.Product!.CostPrice ?? 0
        }).ToListAsync();

        // Ø¥Ø°Ø§ ÙƒØ§Ù† Ù‡Ù†Ø§Ùƒ Ø¨Ø­Ø« Ø£Ùˆ ÙÙ„ØªØ±Ø©ØŒ Ù†ÙØ®Ø±Ø¬ ÙƒÙ„ Ø§Ù„Ù†ØªØ§Ø¦Ø¬ Ù„Ù„Ø³Ù‡ÙˆÙ„Ø©ØŒ ÙˆØ¥Ø°Ø§ Ù„Ù… ØªÙƒÙ† Ù‡Ù†Ø§Ùƒ ÙÙ„ØªØ±Ø©ØŒ Ù†Ù„ØªØ²Ù… Ø¨Ø§Ù„Ø¹Ø¯Ø¯ Ø§Ù„Ø¹Ø´ÙˆØ§Ø¦ÙŠ
        var isFiltered = !string.IsNullOrEmpty(search) || categoryId.HasValue || brandId.HasValue || !string.IsNullOrEmpty(color) || !string.IsNullOrEmpty(size);
        
        IEnumerable<object> picked;
        if (isFiltered)
        {
            picked = allVariants;
        }
        else
        {
            var rng = new Random(today.DayOfYear + today.Month * 100 + today.Year);
            picked = allVariants.OrderBy(_ => rng.Next()).Take(count);
        }
        
        var pickedList = picked.ToList();

        return Ok(new
        {
            date  = today.ToString("yyyy-MM-dd"),
            count = pickedList.Count,
            items = pickedList
        });
    }

    [HttpPost("cycle-count-submit")]
    [RequirePermission(ModuleKeys.ReportsMain)]
    public async Task<IActionResult> CycleCountSubmit([FromBody] List<CycleCountItemDto> entries)
    {
        if (entries == null || !entries.Any())
            return BadRequest(new { message = "Ù„Ø§ ØªÙˆØ¬Ø¯ Ø¨ÙŠØ§Ù†Ø§Øª Ù„Ù„Ø¬Ø±Ø¯" });

        var today = TimeHelper.GetEgyptTime();
        var results = new List<object>();
        
        // 1. Create a Master Audit Record for this cycle count
        var audit = new InventoryAudit
        {
            Title = $"Ø¬Ø±Ø¯ ÙŠÙˆÙ…ÙŠ Ø¹Ø´ÙˆØ§Ø¦ÙŠ - {today:yyyy/MM/dd}",
            Description = "Ø¬Ø±Ø¯ Ø³Ø±ÙŠØ¹ Ù„Ù„Ø£ØµÙ†Ø§Ù Ø§Ù„Ù…Ø¬Ø¯ÙˆÙ„Ø© Ø§Ù„ÙŠÙˆÙ…",
            AuditDate = today,
            Status = InventoryAuditStatus.Posted,
            Items = new List<InventoryAuditItem>()
        };

        decimal totalExpectedValue = 0;
        decimal totalActualValue = 0;

        foreach (var entry in entries)
        {
            var variant = await _db.ProductVariants
                .Include(v => v.Product)
                .FirstOrDefaultAsync(v => v.Id == entry.VariantId);

            if (variant == null) continue;

            var oldStock = variant.StockQuantity;
            var diff = entry.ActualCount - oldStock;
            var unitCost = variant.Product?.CostPrice ?? 0;

            totalExpectedValue += oldStock * unitCost;
            totalActualValue += entry.ActualCount * unitCost;

            // Add to Audit Items
            audit.Items.Add(new InventoryAuditItem
            {
                ProductId = variant.ProductId,
                ProductVariantId = variant.Id,
                ExpectedQuantity = oldStock,
                ActualQuantity = entry.ActualCount,
                UnitCost = unitCost,
                Note = entry.Notes
            });

            if (diff != 0)
            {
                _db.InventoryMovements.Add(new InventoryMovement
                {
                    ProductId = variant.ProductId,
                    ProductVariantId = variant.Id,
                    Type = InventoryMovementType.Audit,
                    Quantity = diff,
                    RemainingStock = entry.ActualCount,
                    Reference = $"CYCLE-{today:yyyyMMdd}",
                    Note = entry.Notes ?? "Ø¬Ø±Ø¯ Ø¬Ø²Ø¦ÙŠ ÙŠÙˆÙ…ÙŠ ØªÙ„Ù‚Ø§Ø¦ÙŠ",
                    UnitCost = unitCost,
                    CreatedAt = today
                });

                variant.StockQuantity = entry.ActualCount;

                if (variant.Product != null)
                {
                    var otherStock = await _db.ProductVariants
                        .Where(v => v.ProductId == variant.ProductId && v.Id != variant.Id)
                        .SumAsync(v => v.StockQuantity);
                    variant.Product.TotalStock = otherStock + entry.ActualCount;
                }
            }

            results.Add(new
            {
                entry.VariantId,
                OldStock = oldStock,
                ActualCount = entry.ActualCount,
                Difference = diff,
                HasDifference = diff != 0
            });
        }

        audit.TotalExpectedValue = totalExpectedValue;
        audit.TotalActualValue = totalActualValue;
        _db.InventoryAudits.Add(audit);

        await _db.SaveChangesAsync();

        // â”€â”€ ACCOUNTING LINK â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        decimal netImpact = audit.TotalActualValue - audit.TotalExpectedValue;
        if (netImpact != 0)
        {
            try {
                var je = await _accounting.PostInventoryAdjustmentAsync(0, netImpact, $"AUDIT-{audit.Id}", User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                if (je.HasValue)
                {
                    audit.JournalEntryId = je.Value;
                    await _db.SaveChangesAsync();
                }
            } catch { /* Ignore mappings error */ }
        }

        int withDiff = results.Count(r =>
        {
            var prop = r.GetType().GetProperty("HasDifference");
            return prop != null && (bool)(prop.GetValue(r) ?? false);
        });

        return Ok(new
        {
            auditId = audit.Id,
            submittedAt = today.ToString("yyyy-MM-dd HH:mm"),
            totalItems = entries.Count,
            withDifferences = withDiff,
            results
        });
    }
}

// â”€â”€ Cycle Count DTO â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
public record CycleCountItemDto(int VariantId, int ActualCount, string? Notes);

