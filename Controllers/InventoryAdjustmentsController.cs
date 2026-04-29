using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using System.Security.Claims;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.Inventory, requireEdit: true)]
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
    /// ØªØ³ÙˆÙŠØ© Ù…Ø®Ø²Ù†ÙŠØ© Ø³Ø±ÙŠØ¹Ø© Ù„ØµÙ†Ù ÙˆØ§Ø­Ø¯
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Adjust([FromBody] QuickAdjustmentDto dto)
    {
        if (dto.Quantity == 0) return BadRequest("Ø§Ù„ÙƒÙ…ÙŠØ© Ù„Ø§ ÙŠÙ…ÙƒÙ† Ø£Ù† ØªÙƒÙˆÙ† ØµÙØ±");

        await _inventory.LogMovementAsync(
            InventoryMovementType.Adjustment,
            dto.Quantity,
            dto.ProductId,
            dto.ProductVariantId,
            "MANUAL-ADJ",
            dto.Note ?? "ØªØ¹Ø¯ÙŠÙ„ ÙŠØ¯ÙˆÙŠ Ù…Ù† Ø§Ù„Ø¥Ø¯Ø§Ø±Ø©",
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        );

        await _db.SaveChangesAsync();
        return Ok(new { message = "ØªÙ… ØªØ¹Ø¯ÙŠÙ„ Ø§Ù„Ù…Ø®Ø²ÙˆÙ† Ø¨Ù†Ø¬Ø§Ø­" });
    }
}

public record QuickAdjustmentDto(int? ProductId, int? ProductVariantId, int Quantity, string? Note);

