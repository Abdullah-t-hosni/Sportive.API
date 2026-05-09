using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.ProductsGroup, requireEdit: true)]
public class SizeGroupsController : ControllerBase
{
    private readonly AppDbContext _db;
    public SizeGroupsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _db.SizeGroups.Include(g => g.Values).ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var group = await _db.SizeGroups.Include(g => g.Values).FirstOrDefaultAsync(g => g.Id == id);
        return group == null ? NotFound() : Ok(group);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SizeGroup group)
    {
        _db.SizeGroups.Add(group);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = group.Id }, group);
    }

        [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] SizeGroup group)
    {
        if (id != group.Id) return BadRequest();
        
        var existingGroup = await _db.SizeGroups.Include(g => g.Values).FirstOrDefaultAsync(g => g.Id == id);
        if (existingGroup == null) return NotFound();

        existingGroup.Name = group.Name;
        existingGroup.Description = group.Description;
        existingGroup.UpdatedAt = Sportive.API.Utils.TimeHelper.GetEgyptTime();

        _db.SizeValues.RemoveRange(existingGroup.Values);
        if (group.Values != null)
        {
            foreach (var v in group.Values)
            {
                v.Id = 0;
                v.SizeGroupId = id;
            }
            _db.SizeValues.AddRange(group.Values);
        }

        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { if (!await _db.SizeGroups.AnyAsync(e => e.Id == id)) return NotFound(); throw; }
        
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var group = await _db.SizeGroups.FindAsync(id);
        if (group == null) return NotFound();
        _db.SizeGroups.Remove(group);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}


