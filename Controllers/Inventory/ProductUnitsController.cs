using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.Units)]
public class ProductUnitsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITranslator _t;

    public ProductUnitsController(AppDbContext db, ITranslator t)
    {
        _db = db;
        _t = t;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll()
    {
        var count = await _db.ProductUnits.CountAsync();
        if (count == 0)
        {
            var defaultUnits = new List<ProductUnit>
            {
                new ProductUnit { NameAr = "قطعة", NameEn = "Piece", Symbol = "PC", Multiplier = 1, IsActive = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new ProductUnit { NameAr = "دستة", NameEn = "Dozen", Symbol = "DZ", Multiplier = 12, IsActive = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new ProductUnit { NameAr = "نصف دستة", NameEn = "Half Dozen", Symbol = "HDZ", Multiplier = 6, IsActive = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new ProductUnit { NameAr = "علبة", NameEn = "Box", Symbol = "BOX", Multiplier = 24, IsActive = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new ProductUnit { NameAr = "كرتونة", NameEn = "Carton", Symbol = "CTN", Multiplier = 48, IsActive = true, CreatedAt = TimeHelper.GetEgyptTime() },
                new ProductUnit { NameAr = "طقم", NameEn = "Set", Symbol = "SET", Multiplier = 1, IsActive = true, CreatedAt = TimeHelper.GetEgyptTime() }
            };
            _db.ProductUnits.AddRange(defaultUnits);
            await _db.SaveChangesAsync();
        }

        var units = await _db.ProductUnits.OrderBy(u => u.NameAr).ToListAsync();
        return Ok(units);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var unit = await _db.ProductUnits.FindAsync(id);
        if (unit == null) return NotFound();
        return Ok(unit);
    }

    [RequirePermission(ModuleKeys.Units, requireEdit: true)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProductUnit unit)
    {
        if (string.IsNullOrWhiteSpace(unit.NameAr))
            return BadRequest(new { message = _t.Get("Auth.FullNameRequired") }); // Using generic required field key

        unit.CreatedAt = TimeHelper.GetEgyptTime();
        _db.ProductUnits.Add(unit);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = unit.Id }, unit);
    }

    [RequirePermission(ModuleKeys.Units, requireEdit: true)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] ProductUnit dto)
    {
        var unit = await _db.ProductUnits.FindAsync(id);
        if (unit == null) return NotFound();

        unit.NameAr = dto.NameAr;
        unit.NameEn = dto.NameEn;
        unit.Symbol = dto.Symbol;
        unit.Multiplier = dto.Multiplier;
        unit.IsActive = dto.IsActive;
        unit.UpdatedAt = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return Ok(unit);
    }

    [RequirePermission(ModuleKeys.Units, requireEdit: true)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var unit = await _db.ProductUnits.FindAsync(id);
        if (unit == null) return NotFound();

        _db.ProductUnits.Remove(unit);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
