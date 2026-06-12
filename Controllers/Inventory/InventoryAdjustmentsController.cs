using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using System.Security.Claims;
using Sportive.API.Extensions;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.Inventory, requireEdit: true)]
public class InventoryAdjustmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly ITranslator _t;

    public InventoryAdjustmentsController(AppDbContext db, IInventoryService inventory, ITranslator t)
    {
        _db = db;
        _inventory = inventory;
        _t = t;
    }

    /// <summary>
    /// Quick stock adjustment for a single item
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Adjust([FromBody] QuickAdjustmentDto dto)
    {
        if (dto.Quantity == 0) return BadRequest(new { message = _t.Get("Inventory.AdjustmentZeroQty") });

        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? isolatedWarehouseId = canViewAll ? dto.WarehouseId : (User.GetWarehouseId() ?? dto.WarehouseId);

        await _inventory.LogMovementAsync(
            InventoryMovementType.Adjustment,
            dto.Quantity,
            dto.ProductId,
            dto.ProductVariantId,
            "MANUAL-ADJ",
            dto.Note ?? _t.Get("Inventory.ManualAdminAdjustment"),
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            warehouseId: isolatedWarehouseId
        );

        await _db.SaveChangesAsync();
        return Ok(new { message = _t.Get("Inventory.AdjustmentSuccess") });
    }
}

public record QuickAdjustmentDto(int? ProductId, int? ProductVariantId, int Quantity, string? Note, int? WarehouseId = null);
