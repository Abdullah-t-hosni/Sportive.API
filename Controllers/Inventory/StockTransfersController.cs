using System.Security.Claims;
using Sportive.API.Attributes;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Interfaces;
using Sportive.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Utils;
using System.Security.Claims;
using Sportive.API.Extensions;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StockTransfersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IAuditService _audit;

    public StockTransfersController(AppDbContext db, IInventoryService inventory, IAuditService audit)
    {
        _db = db;
        _inventory = inventory;
        _audit = audit;
    }

    [RequirePermission(ModuleKeys.Inventory)]
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var q = _db.StockTransfers
            .Include(t => t.SourceWarehouse)
            .Include(t => t.DestinationWarehouse)
            .AsQueryable();

        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        if (!canViewAll)
        {
            int? isolatedBranchId = User.GetBranchId();
            int? isolatedWarehouseId = User.GetWarehouseId();

            if (isolatedWarehouseId.HasValue)
            {
                q = q.Where(t => t.SourceWarehouseId == isolatedWarehouseId.Value || t.DestinationWarehouseId == isolatedWarehouseId.Value);
            }
            else if (isolatedBranchId.HasValue)
            {
                q = q.Where(t => t.SourceWarehouse.BranchId == isolatedBranchId.Value || t.DestinationWarehouse.BranchId == isolatedBranchId.Value);
            }
        }

        var transfers = await q.OrderByDescending(t => t.CreatedAt).ToListAsync();
        return Ok(transfers);
    }

    [RequirePermission(ModuleKeys.Inventory)]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var transfer = await _db.StockTransfers
            .Include(t => t.SourceWarehouse)
            .Include(t => t.DestinationWarehouse)
            .Include(t => t.Items)
                .ThenInclude(i => i.ProductVariant)
                    .ThenInclude(v => v.Product)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transfer == null) return NotFound();
        return Ok(transfer);
    }

    [RequirePermission(ModuleKeys.Inventory, requireEdit: true)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStockTransferDto dto)
    {
        if (dto.SourceWarehouseId == dto.DestinationWarehouseId)
            return BadRequest(new { message = "Source and destination warehouses must be different." });

        var sourceExists = await _db.Warehouses.AnyAsync(w => w.Id == dto.SourceWarehouseId && w.IsActive);
        var destExists = await _db.Warehouses.AnyAsync(w => w.Id == dto.DestinationWarehouseId && w.IsActive);
        if (!sourceExists || !destExists)
            return BadRequest(new { message = "One or both selected warehouses are invalid or inactive." });

        if (dto.Items == null || !dto.Items.Any())
            return BadRequest(new { message = "Transfer must contain at least one item." });

        // Auto-generate transfer number
        var todayStr = DateTime.Today.ToString("yyyyMMdd");
        var count = await _db.StockTransfers.CountAsync(t => t.TransferNumber.StartsWith($"ST-{todayStr}-"));
        var transferNumber = $"ST-{todayStr}-{(count + 1):D4}";

        var transfer = new StockTransfer
        {
            TransferNumber = transferNumber,
            SourceWarehouseId = dto.SourceWarehouseId,
            DestinationWarehouseId = dto.DestinationWarehouseId,
            Description = dto.Description,
            Status = StockTransferStatus.Draft,
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        foreach (var item in dto.Items)
        {
            if (item.Quantity <= 0)
                return BadRequest(new { message = "Quantity must be greater than zero for all items." });

            var variantExists = await _db.ProductVariants.AnyAsync(v => v.Id == item.ProductVariantId);
            if (!variantExists)
                return BadRequest(new { message = $"Product variant ID {item.ProductVariantId} does not exist." });

            transfer.Items.Add(new StockTransferItem
            {
                ProductVariantId = item.ProductVariantId,
                Quantity = item.Quantity,
                Note = item.Note,
                CreatedAt = TimeHelper.GetEgyptTime()
            });
        }

        _db.StockTransfers.Add(transfer);
        await _db.SaveChangesAsync();
        
        try { await _audit.LogChangeAsync<StockTransfer>("CreateStockTransfer", "StockTransfer", transfer.Id.ToString(), null, transfer, User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

        return CreatedAtAction(nameof(GetById), new { id = transfer.Id }, transfer);
    }

    [RequirePermission(ModuleKeys.Inventory, requireEdit: true)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateStockTransferDto dto)
    {
        var oldTransfer = await _db.StockTransfers.AsNoTracking().Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id);
        var transfer = await _db.StockTransfers
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transfer == null) return NotFound();

        if (transfer.Status != StockTransferStatus.Draft && transfer.Status != StockTransferStatus.Pending)
            return BadRequest(new { message = "Can only update transfers in Draft or Pending status." });

        if (dto.SourceWarehouseId == dto.DestinationWarehouseId)
            return BadRequest(new { message = "Source and destination warehouses must be different." });

        var sourceExists = await _db.Warehouses.AnyAsync(w => w.Id == dto.SourceWarehouseId && w.IsActive);
        var destExists = await _db.Warehouses.AnyAsync(w => w.Id == dto.DestinationWarehouseId && w.IsActive);
        if (!sourceExists || !destExists)
            return BadRequest(new { message = "One or both selected warehouses are invalid or inactive." });

        if (dto.Items == null || !dto.Items.Any())
            return BadRequest(new { message = "Transfer must contain at least one item." });

        // Update transfer properties
        transfer.SourceWarehouseId = dto.SourceWarehouseId;
        transfer.DestinationWarehouseId = dto.DestinationWarehouseId;
        transfer.Description = dto.Description;
        transfer.UpdatedAt = TimeHelper.GetEgyptTime();

        // Clear existing items and re-add
        _db.StockTransferItems.RemoveRange(transfer.Items);
        transfer.Items.Clear();

        foreach (var item in dto.Items)
        {
            if (item.Quantity <= 0)
                return BadRequest(new { message = "Quantity must be greater than zero for all items." });

            var variantExists = await _db.ProductVariants.AnyAsync(v => v.Id == item.ProductVariantId);
            if (!variantExists)
                return BadRequest(new { message = $"Product variant ID {item.ProductVariantId} does not exist." });

            transfer.Items.Add(new StockTransferItem
            {
                ProductVariantId = item.ProductVariantId,
                Quantity = item.Quantity,
                Note = item.Note,
                CreatedAt = TimeHelper.GetEgyptTime()
            });
        }

        await _db.SaveChangesAsync();
        
        try { await _audit.LogChangeAsync<StockTransfer>("UpdateStockTransfer", "StockTransfer", id.ToString(), oldTransfer, transfer, User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

        return Ok(transfer);
    }

    [RequirePermission(ModuleKeys.Inventory, requireEdit: true)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var transfer = await _db.StockTransfers.FindAsync(id);
        if (transfer == null) return NotFound();

        if (transfer.Status != StockTransferStatus.Draft && transfer.Status != StockTransferStatus.Cancelled)
            return BadRequest(new { message = "Only Draft or Cancelled transfers can be deleted." });

        _db.StockTransfers.Remove(transfer);
        await _db.SaveChangesAsync();
        
        try { await _audit.LogChangeAsync<StockTransfer>("DeleteStockTransfer", "StockTransfer", id.ToString(), transfer, null, User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

        return NoContent();
    }

    [RequirePermission(ModuleKeys.Inventory, requireEdit: true)]
    [HttpPost("{id}/ship")]
    public async Task<IActionResult> Ship(int id)
    {
        var oldTransfer = await _db.StockTransfers.AsNoTracking().Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id);
        var transfer = await _db.StockTransfers
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transfer == null) return NotFound();

        if (transfer.Status != StockTransferStatus.Draft && transfer.Status != StockTransferStatus.Pending)
            return BadRequest(new { message = "Only Draft or Pending transfers can be shipped." });

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        using var dbTransaction = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var item in transfer.Items)
            {
                // Deduct stock from source warehouse
                await _inventory.LogMovementAsync(
                    InventoryMovementType.TransferOut,
                    -item.Quantity,
                    variantId: item.ProductVariantId,
                    reference: transfer.TransferNumber,
                    note: $"Stock Transfer Out: {transfer.TransferNumber}",
                    userId: userId,
                    warehouseId: transfer.SourceWarehouseId,
                    autoSave: false
                );
            }

            transfer.Status = StockTransferStatus.Shipped;
            transfer.ShippedByUserId = userId;
            transfer.ShippedAt = TimeHelper.GetEgyptTime();
            transfer.UpdatedAt = TimeHelper.GetEgyptTime();

            await _db.SaveChangesAsync();
            await dbTransaction.CommitAsync();
            
            try { await _audit.LogChangeAsync<StockTransfer>("ShipStockTransfer", "StockTransfer", id.ToString(), oldTransfer, transfer, User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync();
            return BadRequest(new { message = ex.Message });
        }

        return Ok(transfer);
    }

    [RequirePermission(ModuleKeys.Inventory, requireEdit: true)]
    [HttpPost("{id}/receive")]
    public async Task<IActionResult> Receive(int id)
    {
        var oldTransfer = await _db.StockTransfers.AsNoTracking().Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id);
        var transfer = await _db.StockTransfers
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transfer == null) return NotFound();

        if (transfer.Status != StockTransferStatus.Shipped)
            return BadRequest(new { message = "Only Shipped transfers can be received." });

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        using var dbTransaction = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var item in transfer.Items)
            {
                // Add stock to destination warehouse
                await _inventory.LogMovementAsync(
                    InventoryMovementType.TransferIn,
                    item.Quantity,
                    variantId: item.ProductVariantId,
                    reference: transfer.TransferNumber,
                    note: $"Stock Transfer In: {transfer.TransferNumber}",
                    userId: userId,
                    warehouseId: transfer.DestinationWarehouseId,
                    autoSave: false
                );
            }

            transfer.Status = StockTransferStatus.Received;
            transfer.ReceivedByUserId = userId;
            transfer.ReceivedAt = TimeHelper.GetEgyptTime();
            transfer.UpdatedAt = TimeHelper.GetEgyptTime();

            await _db.SaveChangesAsync();
            await dbTransaction.CommitAsync();
            
            try { await _audit.LogChangeAsync<StockTransfer>("ReceiveStockTransfer", "StockTransfer", id.ToString(), oldTransfer, transfer, User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync();
            return BadRequest(new { message = ex.Message });
        }

        return Ok(transfer);
    }

    [RequirePermission(ModuleKeys.Inventory, requireEdit: true)]
    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var oldTransfer = await _db.StockTransfers.AsNoTracking().Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id);
        var transfer = await _db.StockTransfers
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transfer == null) return NotFound();

        if (transfer.Status == StockTransferStatus.Received || transfer.Status == StockTransferStatus.Cancelled)
            return BadRequest(new { message = "Cannot cancel a transfer that has already been received or cancelled." });

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        using var dbTransaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // If it was shipped, we need to return the deducted stock back to the source warehouse
            if (transfer.Status == StockTransferStatus.Shipped)
            {
                foreach (var item in transfer.Items)
                {
                    await _inventory.LogMovementAsync(
                        InventoryMovementType.TransferIn,
                        item.Quantity,
                        variantId: item.ProductVariantId,
                        reference: transfer.TransferNumber,
                        note: $"Cancelled Transfer Return: {transfer.TransferNumber}",
                        userId: userId,
                        warehouseId: transfer.SourceWarehouseId,
                        autoSave: false
                    );
                }
            }

            transfer.Status = StockTransferStatus.Cancelled;
            transfer.UpdatedAt = TimeHelper.GetEgyptTime();

            await _db.SaveChangesAsync();
            await dbTransaction.CommitAsync();
            
            try { await _audit.LogChangeAsync<StockTransfer>("CancelStockTransfer", "StockTransfer", id.ToString(), oldTransfer, transfer, User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync();
            return BadRequest(new { message = ex.Message });
        }

        return Ok(transfer);
    }
}

public class CreateStockTransferDto
{
    public int SourceWarehouseId { get; set; }
    public int DestinationWarehouseId { get; set; }
    public string? Description { get; set; }
    public List<CreateStockTransferItemDto> Items { get; set; } = new();
}

public class CreateStockTransferItemDto
{
    public int ProductVariantId { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
}
