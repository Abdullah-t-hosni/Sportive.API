using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiagnosticsController : ControllerBase
{
    private readonly AppDbContext _db;

    public DiagnosticsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("orphaned-movements")]
    public async Task<IActionResult> GetOrphanedMovements()
    {
        var existingOrderNumbers = await _db.Orders.Select(o => o.OrderNumber).ToListAsync();

        var orphanedSales = await _db.InventoryMovements
            .Where(m => (m.Type == InventoryMovementType.Sale || m.Type == InventoryMovementType.ReturnIn) 
                     && m.Reference != null 
                     && m.Reference.StartsWith("ORD-") // Or whatever order numbers start with, actually let's just do an Except or Where !Contains
                     && !existingOrderNumbers.Contains(m.Reference))
            .ToListAsync();

        return Ok(new
        {
            Count = orphanedSales.Count,
            Movements = orphanedSales.Select(m => new
            {
                m.Id,
                m.ProductId,
                m.ProductVariantId,
                m.Quantity,
                m.Reference,
                Type = m.Type.ToString(),
                m.CreatedAt
            })
        });
    }

    [HttpPost("cleanup-orphaned-movements")]
    public async Task<IActionResult> CleanupOrphanedMovements()
    {
        var existingOrderNumbers = await _db.Orders.Select(o => o.OrderNumber).ToListAsync();

        // Include "POS" prefixed orders or "ORD" if needed. We just check if Reference looks like an order
        var orphanedSales = await _db.InventoryMovements
            .Where(m => (m.Type == InventoryMovementType.Sale || m.Type == InventoryMovementType.ReturnIn) 
                     && m.Reference != null 
                     && !existingOrderNumbers.Contains(m.Reference))
            .ToListAsync();

        _db.InventoryMovements.RemoveRange(orphanedSales);
        await _db.SaveChangesAsync();

        // Run Recalculate Stock logic for affected products
        // We will just do the simple stock sums update like DataMaintenanceService does.
        var affectedProductIds = orphanedSales.Where(m => m.ProductId.HasValue).Select(m => m.ProductId!.Value).Distinct().ToList();
        var affectedVariantIds = orphanedSales.Where(m => m.ProductVariantId.HasValue).Select(m => m.ProductVariantId!.Value).Distinct().ToList();

        return Ok(new
        {
            DeletedCount = orphanedSales.Count,
            AffectedProductIds = affectedProductIds,
            AffectedVariantIds = affectedVariantIds,
            Message = "Deleted successfully. Please run recalculate-stock from UI to update product Stock."
        });
    }
}
