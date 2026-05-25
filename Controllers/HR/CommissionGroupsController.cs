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
[Route("api/commissions/groups")]
[RequirePermission(ModuleKeys.HrPayroll)]
public class CommissionGroupsController : ControllerBase
{
    private readonly AppDbContext _db;
    public CommissionGroupsController(AppDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CommissionGroupDto>>> GetGroups()
    {
        var groups = await _db.CommissionGroups
            .Include(g => g.Members)
            .ToListAsync();

        var result = groups.Select(g => new CommissionGroupDto(
            g.Id,
            g.Name,
            g.Description,
            g.CommissionSchemeId,
            g.Members.Select(m => m.Id).ToList()
        )).ToList();

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CommissionGroupDto>> GetGroup(int id)
    {
        var g = await _db.CommissionGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (g == null) return NotFound();

        return Ok(new CommissionGroupDto(
            g.Id,
            g.Name,
            g.Description,
            g.CommissionSchemeId,
            g.Members.Select(m => m.Id).ToList()
        ));
    }

    [HttpPost]
    public async Task<ActionResult<CommissionGroupDto>> CreateGroup(CreateCommissionGroupDto dto)
    {
        var g = new CommissionGroup
        {
            Name = dto.Name,
            Description = dto.Description,
            CommissionSchemeId = dto.CommissionSchemeId,
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        if (dto.MemberIds != null && dto.MemberIds.Any())
        {
            var members = await _db.Employees.Where(e => dto.MemberIds.Contains(e.Id)).ToListAsync();
            g.Members = members;
        }

        _db.CommissionGroups.Add(g);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetGroup), new { id = g.Id }, new CommissionGroupDto(
            g.Id,
            g.Name,
            g.Description,
            g.CommissionSchemeId,
            g.Members.Select(m => m.Id).ToList()
        ));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateGroup(int id, CreateCommissionGroupDto dto)
    {
        var g = await _db.CommissionGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (g == null) return NotFound();

        g.Name = dto.Name;
        g.Description = dto.Description;
        g.CommissionSchemeId = dto.CommissionSchemeId;
        g.UpdatedAt = TimeHelper.GetEgyptTime();

        // Update Members
        g.Members.Clear();
        if (dto.MemberIds != null && dto.MemberIds.Any())
        {
            var members = await _db.Employees.Where(e => dto.MemberIds.Contains(e.Id)).ToListAsync();
            g.Members = members;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGroup(int id)
    {
        var g = await _db.CommissionGroups.FindAsync(id);
        if (g == null) return NotFound();

        _db.CommissionGroups.Remove(g);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
