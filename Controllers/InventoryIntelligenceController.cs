using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using System.Security.Claims;

namespace Sportive.API.Controllers;

/// <summary>
/// Inventory Intelligence Controller — Phase 2 Enhancements
/// يحتوي على: جدول استحقاقات الموردين، تنبيهات المقاسات، الجرد الجزئي اليومي
/// </summary>
[ApiController]
[Route("api/operationalreports")]
[Authorize(Roles = "Admin,Manager,Accountant,Staff")]
public class InventoryIntelligenceController : ControllerBase
{
    private readonly AppDbContext _db;
    public InventoryIntelligenceController(AppDbContext db) => _db = db;

    // ══════════════════════════════════════════════════════
    // 13. جدول استحقاقات الموردين الأسبوعي (Payables Weekly Schedule)
    // GET /api/operationalreports/payables-schedule?weeks=4
    // ══════════════════════════════════════════════════════
    [HttpGet("payables-schedule")]
    public async Task<IActionResult> PayablesSchedule([FromQuery] int weeks = 4)
    {
        var today = TimeHelper.GetEgyptTime().Date;

        var invoices = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Where(i => i.Status != PurchaseInvoiceStatus.Cancelled
                     && i.Status != PurchaseInvoiceStatus.Returned
                     && i.RemainingAmount > 0)
            .ToListAsync();

        var buckets = new List<object>();
        decimal grandTotal = 0;

        // ── سطل المتأخرات (Overdue) ─────────────────────
        var overdueList = invoices
            .Where(i => i.DueDate.HasValue && i.DueDate.Value.Date < today)
            .ToList();
        var overdueTot = overdueList.Sum(i => i.RemainingAmount);
        grandTotal += overdueTot;

        buckets.Add(new
        {
            week      = 0,
            label     = "متأخرة (Overdue)",
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

        // ── أسابيع مستقبلية ──────────────────────────────
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
                label     = $"أسبوع {w} ({wStart:MM/dd} - {wEnd:MM/dd})",
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

        // ── فواتير آجلة بدون تاريخ استحقاق ─────────────
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

    // ══════════════════════════════════════════════════════
    // 14. تنبيهات نقص المخزون على مستوى المقاس/اللون
    // GET /api/operationalreports/variant-reorder-alerts?threshold=2
    // ══════════════════════════════════════════════════════
    [HttpGet("variant-reorder-alerts")]
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

    // ══════════════════════════════════════════════════════
    // 15. الجرد الجزئي اليومي (Daily Cycle Count)
    // GET  /api/operationalreports/cycle-count-today?count=5
    // POST /api/operationalreports/cycle-count-submit
    // ══════════════════════════════════════════════════════
    [HttpGet("cycle-count-today")]
    public async Task<IActionResult> CycleCountToday([FromQuery] int count = 5)
    {
        var today = TimeHelper.GetEgyptTime().Date;

        // الـ variants التي جُردت اليوم بالفعل بالجرد الجزئي
        var auditedToday = await _db.InventoryMovements
            .Where(m => m.Type == InventoryMovementType.Audit
                     && m.CreatedAt >= today
                     && m.Reference != null
                     && m.Reference.StartsWith("CYCLE-"))
            .Select(m => m.ProductVariantId)
            .Distinct()
            .ToListAsync();

        // seed ثابت لليوم (للاتساق في حالة إعادة الفتح)
        var rng = new Random(today.DayOfYear + today.Month * 100 + today.Year);

        var allVariants = await _db.ProductVariants
            .Include(v => v.Product)
            .Where(v => v.Product != null
                     && v.Product.Status == ProductStatus.Active
                     && !auditedToday.Contains(v.Id))
            .Select(v => new
            {
                VariantId   = v.Id,
                ProductId   = v.ProductId,
                ProductName = v.Product!.NameAr,
                SKU         = v.Product!.SKU,
                Size        = v.Size,
                Color       = v.ColorAr ?? v.Color,
                SystemStock = v.StockQuantity,
                CostPrice   = v.Product!.CostPrice ?? 0
            })
            .ToListAsync();

        var picked = allVariants.OrderBy(_ => rng.Next()).Take(count).ToList();

        return Ok(new
        {
            date  = today.ToString("yyyy-MM-dd"),
            count = picked.Count,
            items = picked
        });
    }

    [HttpPost("cycle-count-submit")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    public async Task<IActionResult> CycleCountSubmit([FromBody] List<CycleCountItemDto> entries)
    {
        if (entries == null || !entries.Any())
            return BadRequest(new { message = "لا توجد بيانات للجرد" });

        var today   = TimeHelper.GetEgyptTime();
        var results = new List<object>();

        foreach (var entry in entries)
        {
            var variant = await _db.ProductVariants
                .Include(v => v.Product)
                .FirstOrDefaultAsync(v => v.Id == entry.VariantId);

            if (variant == null) continue;

            var oldStock = variant.StockQuantity;
            var diff     = entry.ActualCount - oldStock;

            if (diff != 0)
            {
                _db.InventoryMovements.Add(new InventoryMovement
                {
                    ProductId        = variant.ProductId,
                    ProductVariantId = variant.Id,
                    Type             = InventoryMovementType.Audit,
                    Quantity         = diff,
                    RemainingStock   = entry.ActualCount,
                    Reference        = $"CYCLE-{today:yyyyMMdd}",
                    Note             = entry.Notes ?? "جرد جزئي يومي تلقائي",
                    UnitCost         = variant.Product?.CostPrice ?? 0,
                    CreatedAt        = today
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
                OldStock      = oldStock,
                ActualCount   = entry.ActualCount,
                Difference    = diff,
                HasDifference = diff != 0
            });
        }

        await _db.SaveChangesAsync();

        int withDiff = results.Count(r =>
        {
            var prop = r.GetType().GetProperty("HasDifference");
            return prop != null && (bool)(prop.GetValue(r) ?? false);
        });

        return Ok(new
        {
            submittedAt     = today.ToString("yyyy-MM-dd HH:mm"),
            totalItems      = entries.Count,
            withDifferences = withDiff,
            results
        });
    }
}

// ── Cycle Count DTO ──────────────────────────────────────
public record CycleCountItemDto(int VariantId, int ActualCount, string? Notes);
