using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/commissions/schemes")]
[RequirePermission(ModuleKeys.HrPayroll)]
public class CommissionSchemesController : ControllerBase
{
    private readonly AppDbContext _db;
    public CommissionSchemesController(AppDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CommissionSchemeDto>>> GetSchemes()
    {
        var schemes = await _db.CommissionSchemes
            .Include(s => s.Tiers)
            .Select(s => new CommissionSchemeDto(
                s.Id,
                s.Name,
                s.Type,
                s.Basis,
                s.DefaultRate,
                s.TargetAmount,
                s.Tiers.Select(t => new CommissionTierDto(t.Id, t.MinAmount, t.MaxAmount, t.Rate)).ToList()
            ))
            .ToListAsync();

        return Ok(schemes);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CommissionSchemeDto>> GetScheme(int id)
    {
        var s = await _db.CommissionSchemes
            .Include(s => s.Tiers)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (s == null) return NotFound();

        return Ok(new CommissionSchemeDto(
            s.Id,
            s.Name,
            s.Type,
            s.Basis,
            s.DefaultRate,
            s.TargetAmount,
            s.Tiers.Select(t => new CommissionTierDto(t.Id, t.MinAmount, t.MaxAmount, t.Rate)).ToList()
        ));
    }

    [HttpPost]
    public async Task<ActionResult<CommissionSchemeDto>> CreateScheme(UpdateCommissionSchemeDto dto)
    {
        var s = new CommissionScheme
        {
            Name = dto.Name,
            Type = dto.Type,
            Basis = dto.Basis,
            DefaultRate = dto.DefaultRate,
            TargetAmount = dto.TargetAmount,
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        s.Tiers = dto.Tiers.Select(t => new CommissionSchemeTier
        {
            MinAmount = t.MinAmount,
            MaxAmount = t.MaxAmount,
            Rate = t.Rate,
            CreatedAt = TimeHelper.GetEgyptTime()
        }).ToList();

        _db.CommissionSchemes.Add(s);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetScheme), new { id = s.Id }, new CommissionSchemeDto(
            s.Id,
            s.Name,
            s.Type,
            s.Basis,
            s.DefaultRate,
            s.TargetAmount,
            s.Tiers.Select(t => new CommissionTierDto(t.Id, t.MinAmount, t.MaxAmount, t.Rate)).ToList()
        ));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateScheme(int id, UpdateCommissionSchemeDto dto)
    {
        var s = await _db.CommissionSchemes
            .Include(s => s.Tiers)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (s == null) return NotFound();

        s.Name = dto.Name;
        s.Type = dto.Type;
        s.Basis = dto.Basis;
        s.DefaultRate = dto.DefaultRate;
        s.TargetAmount = dto.TargetAmount;

        s.Tiers.Clear();
        foreach (var t in dto.Tiers)
        {
            s.Tiers.Add(new CommissionSchemeTier
            {
                MinAmount = t.MinAmount,
                MaxAmount = t.MaxAmount,
                Rate = t.Rate,
                CreatedAt = TimeHelper.GetEgyptTime()
            });
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteScheme(int id)
    {
        var s = await _db.CommissionSchemes.FindAsync(id);
        if (s == null) return NotFound();

        // Delete settings for employees linked to this scheme
        var linkedSettings = await _db.EmployeeCommissionSettings
            .Where(ecs => ecs.CommissionSchemeId == id)
            .ToListAsync();
        _db.EmployeeCommissionSettings.RemoveRange(linkedSettings);

        _db.CommissionSchemes.Remove(s);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
