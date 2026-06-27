using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Models;
using Sportive.API.Data;
using System.Security.Claims;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/system/[controller]")]
[Authorize(Roles = AppRoles.SuperAdmin)]
public class RolesController : ControllerBase
{
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly AppDbContext _db;

    public RolesController(RoleManager<IdentityRole> roleManager, AppDbContext db)
    {
        _roleManager = roleManager;
        _db = db;
    }

    /// <summary>GET /api/system/roles — جلب جميع الأدوار مع أعداد المستخدمين</summary>
    [HttpGet]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _roleManager.Roles.ToListAsync();
        var result = new List<object>();

        foreach (var role in roles)
        {
            // Calculate users in role
            var userCount = await _db.UserRoles.CountAsync(ur => ur.RoleId == role.Id);
            
            result.Add(new
            {
                role.Id,
                role.Name,
                UsersCount = userCount,
                CreatedAt = TimeHelper.GetEgyptTime() // Mock creation time since IdentityRole doesn't have it by default
            });
        }

        return Ok(result);
    }

    /// <summary>POST /api/system/roles — إنشاء دور جديد</summary>
    [HttpPost]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Role name is required.");
        
        var exists = await _roleManager.RoleExistsAsync(dto.Name);
        if (exists) return BadRequest("Role already exists.");

        var role = new IdentityRole(dto.Name);
        var result = await _roleManager.CreateAsync(role);

        if (!result.Succeeded) return BadRequest(result.Errors);

        return Ok(new { role.Id, role.Name, UsersCount = 0 });
    }

    /// <summary>PUT /api/system/roles/{id} — تعديل دور (تغيير اسمه)</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateRole(string id, [FromBody] CreateRoleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Role name is required.");

        var role = await _roleManager.FindByIdAsync(id);
        if (role == null) return NotFound("Role not found.");

        if (role.Name == AppRoles.SuperAdmin) return BadRequest("Cannot modify SuperAdmin role.");

        role.Name = dto.Name;
        var result = await _roleManager.UpdateAsync(role);

        if (!result.Succeeded) return BadRequest(result.Errors);

        return Ok(new { role.Id, role.Name });
    }

    /// <summary>DELETE /api/system/roles/{id} — حذف دور</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRole(string id)
    {
        var role = await _roleManager.FindByIdAsync(id);
        if (role == null) return NotFound("Role not found.");

        if (role.Name == AppRoles.SuperAdmin) return BadRequest("Cannot delete SuperAdmin role.");

        var userCount = await _db.UserRoles.CountAsync(ur => ur.RoleId == role.Id);
        if (userCount > 0) return BadRequest("Cannot delete role because it has users assigned.");

        var result = await _roleManager.DeleteAsync(role);
        if (!result.Succeeded) return BadRequest(result.Errors);

        return NoContent();
    }
}

public class CreateRoleDto
{
    public string Name { get; set; } = null!;
}
