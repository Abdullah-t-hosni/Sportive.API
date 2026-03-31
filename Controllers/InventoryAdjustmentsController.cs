using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using System.Security.Claims;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager")]
public class InventoryAdjustmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;

    public InventoryAdjustmentsController(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    /// <summary>
    /// تسوية مخزنية سريعة لصنف واحد
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Adjust([FromBody] QuickAdjustmentDto dto)
    {
        if (dto.Quantity == 0) return BadRequest("الكمية لا يمكن أن تكون صفر");

        await _inventory.LogMovementAsync(
            InventoryMovementType.Adjustment,
            dto.Quantity,
            dto.ProductId,
            dto.ProductVariantId,
            "MANUAL-ADJ",
            dto.Note ?? "تعديل يدوي من الإدارة",
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        );

        await _db.SaveChangesAsync();
        return Ok(new { message = "تم تعديل المخزون بنجاح" });
    }
}

public record QuickAdjustmentDto(int? ProductId, int? ProductVariantId, int Quantity, string? Note);
