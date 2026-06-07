using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.ColorGroups)]
public class ColorGroupsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ColorGroupsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _db.ColorGroups.Include(g => g.Values).ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var group = await _db.ColorGroups.Include(g => g.Values).FirstOrDefaultAsync(g => g.Id == id);
        return group == null ? NotFound() : Ok(group);
    }

    [RequirePermission(ModuleKeys.ColorGroups, requireEdit: true)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ColorGroup group)
    {
        _db.ColorGroups.Add(group);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = group.Id }, group);
    }

    [RequirePermission(ModuleKeys.ColorGroups, requireEdit: true)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] ColorGroup group)
    {
        if (id != group.Id) return BadRequest();
        
        var existingGroup = await _db.ColorGroups.Include(g => g.Values).FirstOrDefaultAsync(g => g.Id == id);
        if (existingGroup == null) return NotFound();

        existingGroup.Name = group.Name;
        existingGroup.Description = group.Description;
        existingGroup.UpdatedAt = Sportive.API.Utils.TimeHelper.GetEgyptTime();

        _db.ColorValues.RemoveRange(existingGroup.Values);
        if (group.Values != null)
        {
            foreach (var v in group.Values)
            {
                v.Id = 0;
                v.ColorGroupId = id;
            }
            _db.ColorValues.AddRange(group.Values);
        }

        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { if (!await _db.ColorGroups.AnyAsync(e => e.Id == id)) return NotFound(); throw; }
        
        return NoContent();
    }

    [RequirePermission(ModuleKeys.ColorGroups, requireEdit: true)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var group = await _db.ColorGroups.FindAsync(id);
        if (group == null) return NotFound();
        _db.ColorGroups.Remove(group);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
