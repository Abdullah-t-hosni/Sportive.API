using System.Security.Claims;
using Sportive.API.Attributes;
using Sportive.API.Data;
using Sportive.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Utils;
using Sportive.API.Extensions;
using Sportive.API.Interfaces;
using Sportive.API.Services;
using System.Security.Claims;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WarehousesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public WarehousesController(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? branchId)
    {
        var query = _db.Warehouses.Include(w => w.Branch).AsQueryable();

        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        if (!canViewAll)
        {
            int? isolatedBranchId = User.GetBranchId();
            if (isolatedBranchId.HasValue)
            {
                query = query.Where(w => w.BranchId == isolatedBranchId.Value);
            }
        }
        else if (branchId.HasValue)
        {
            query = query.Where(w => w.BranchId == branchId.Value);
        }

        var warehouses = await query.OrderBy(w => w.Name).ToListAsync();
        return Ok(warehouses);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var warehouse = await _db.Warehouses.Include(w => w.Branch).FirstOrDefaultAsync(w => w.Id == id);
        if (warehouse == null) return NotFound();
        return Ok(warehouse);
    }

    [RequirePermission(ModuleKeys.Settings, requireEdit: true)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] WarehouseDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Warehouse name is required." });

        var branchExists = await _db.Branches.AnyAsync(b => b.Id == dto.BranchId);
        if (!branchExists)
            return BadRequest(new { message = "Associated branch not found." });

        var warehouse = new Warehouse
        {
            Name = dto.Name.Trim(),
            Location = dto.Location?.Trim(),
            BranchId = dto.BranchId,
            IsActive = dto.IsActive,
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync();
        
        try { await _audit.LogAsync("CreateWarehouse", "Warehouse", warehouse.Id.ToString(), $"Created warehouse: {warehouse.Name}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

        return CreatedAtAction(nameof(GetById), new { id = warehouse.Id }, warehouse);
    }

    [RequirePermission(ModuleKeys.Settings, requireEdit: true)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] WarehouseDto dto)
    {
        var warehouse = await _db.Warehouses.FindAsync(id);
        if (warehouse == null) return NotFound();

        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Warehouse name is required." });

        var branchExists = await _db.Branches.AnyAsync(b => b.Id == dto.BranchId);
        if (!branchExists)
            return BadRequest(new { message = "Associated branch not found." });

        warehouse.Name = dto.Name.Trim();
        warehouse.Location = dto.Location?.Trim();
        warehouse.BranchId = dto.BranchId;
        warehouse.IsActive = dto.IsActive;
        warehouse.UpdatedAt = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        
        try { await _audit.LogAsync("UpdateWarehouse", "Warehouse", id.ToString(), $"Updated warehouse: {warehouse.Name}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

        return Ok(warehouse);
    }

    [RequirePermission(ModuleKeys.Settings, requireEdit: true)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var warehouse = await _db.Warehouses.FindAsync(id);
        if (warehouse == null) return NotFound();

        var hasStock = await _db.ProductWarehouseStocks.AnyAsync(s => s.WarehouseId == id && s.Quantity > 0);
        if (hasStock)
            return BadRequest(new { message = "Cannot delete warehouse with active product stock. Transfer stock first." });

        var hasOrders = await _db.Orders.AnyAsync(o => o.WarehouseId == id);
        if (hasOrders)
            return BadRequest(new { message = "Cannot delete warehouse linked to historic orders." });

        var hasTransfers = await _db.StockTransfers.AnyAsync(t => t.SourceWarehouseId == id || t.DestinationWarehouseId == id);
        if (hasTransfers)
            return BadRequest(new { message = "Cannot delete warehouse linked to stock transfers." });

        _db.Warehouses.Remove(warehouse);
        await _db.SaveChangesAsync();
        
        try { await _audit.LogAsync("DeleteWarehouse", "Warehouse", id.ToString(), $"Deleted warehouse: {warehouse.Name}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

        return NoContent();
    }
}

public class WarehouseDto
{
    public string Name { get; set; } = string.Empty;
    public string? Location { get; set; }
    public int BranchId { get; set; }
    public bool IsActive { get; set; } = true;
}
