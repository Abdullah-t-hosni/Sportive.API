using Sportive.API.Models;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categories;
    private readonly IAuditService _audit;
    public CategoriesController(ICategoryService categories, IAuditService audit)
    {
        _categories = categories;
        _audit = audit;
    }

    /// <summary>Flat list of all categories (includes parent info and direct children)</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _categories.GetAllAsync());

    /// <summary>Hierarchical tree â€” roots only, with nested children</summary>
    [HttpGet("tree")]
    public async Task<IActionResult> GetTree() =>
        Ok(await _categories.GetTreeAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var cat = await _categories.GetByIdAsync(id);
        return cat == null ? NotFound() : Ok(cat);
    }

    [RequirePermission(ModuleKeys.Categories, requireEdit: true)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryDto dto)
    {
        try
        {
            var cat = await _categories.CreateAsync(dto);
            try { await _audit.LogAsync("CreateCategory", "Category", cat.Id.ToString(), $"Created category {cat.NameAr}", System.Security.Claims.ClaimTypes.NameIdentifier, System.Security.Claims.ClaimTypes.Name); } catch { }
            return CreatedAtAction(nameof(GetById), new { id = cat.Id }, cat);
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [RequirePermission(ModuleKeys.Categories, requireEdit: true)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateCategoryDto dto)
    {
        try 
        { 
            var cat = await _categories.UpdateAsync(id, dto);
            try { await _audit.LogAsync("UpdateCategory", "Category", id.ToString(), $"Updated category", System.Security.Claims.ClaimTypes.NameIdentifier, System.Security.Claims.ClaimTypes.Name); } catch { }
            return Ok(cat); 
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [RequirePermission(ModuleKeys.Categories, requireEdit: true)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try 
        { 
            await _categories.DeleteAsync(id); 
            try { await _audit.LogAsync("DeleteCategory", "Category", id.ToString(), $"Deleted category", System.Security.Claims.ClaimTypes.NameIdentifier, System.Security.Claims.ClaimTypes.Name); } catch { }
            return NoContent(); 
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}

