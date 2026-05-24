using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/shipping-zones")]
public class ShippingZonesController : ControllerBase
{
    private readonly AppDbContext _db;

    public ShippingZonesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var zones = await _db.ShippingZones
            .OrderBy(z => z.Id)
            .ToListAsync();
        return Ok(zones);
    }

    [RequirePermission(ModuleKeys.Settings, requireEdit: true)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ShippingZone zone)
    {
        zone.CreatedAt = TimeHelper.GetEgyptTime();
        _db.ShippingZones.Add(zone);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new { id = zone.Id }, zone);
    }

    [RequirePermission(ModuleKeys.Settings, requireEdit: true)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] ShippingZone dto)
    {
        var zone = await _db.ShippingZones.FindAsync(id);
        if (zone == null) return NotFound();

        zone.NameAr = dto.NameAr;
        zone.NameEn = dto.NameEn;
        zone.Governorates = dto.Governorates;
        zone.Fee = dto.Fee;
        zone.FreeThreshold = dto.FreeThreshold;
        zone.IsActive = dto.IsActive;
        zone.EstimatedDays = dto.EstimatedDays;
        zone.UpdatedAt = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return Ok(zone);
    }

    [RequirePermission(ModuleKeys.Settings, requireEdit: true)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var zone = await _db.ShippingZones.FindAsync(id);
        if (zone == null) return NotFound();

        _db.ShippingZones.Remove(zone);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [RequirePermission(ModuleKeys.Settings, requireEdit: true)]
    [HttpPatch("{id}/toggle")]
    public async Task<IActionResult> Toggle(int id)
    {
        var zone = await _db.ShippingZones.FindAsync(id);
        if (zone == null) return NotFound();

        zone.IsActive = !zone.IsActive;
        zone.UpdatedAt = TimeHelper.GetEgyptTime();
        
        await _db.SaveChangesAsync();
        return Ok(new { zone.IsActive });
    }
}

