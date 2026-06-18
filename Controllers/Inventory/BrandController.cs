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
public class BrandController : ControllerBase
{
    private readonly IBrandService _brands;
    private readonly IAuditService _audit;
    
    public BrandController(IBrandService brands, IAuditService audit) 
    {
        _brands = brands;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _brands.GetAllAsync());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var brand = await _brands.GetByIdAsync(id);
        if (brand == null) 
            return NotFound();
            
        return Ok(brand);
    }

    [RequirePermission(ModuleKeys.Brands, requireEdit: true)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBrandDto dto)
    {
        try
        {
            var brand = await _brands.CreateAsync(dto);
            try { await _audit.LogAsync("CreateBrand", "Brand", brand.Id.ToString(), $"Created brand {brand.NameAr}", System.Security.Claims.ClaimTypes.NameIdentifier, System.Security.Claims.ClaimTypes.Name); } catch { }
            return CreatedAtAction(nameof(GetById), new { id = brand.Id }, brand);
        }
        catch (ArgumentException ex) 
        { 
            return BadRequest(new { message = ex.Message }); 
        }
    }

    [RequirePermission(ModuleKeys.Brands, requireEdit: true)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateBrandDto dto)
    {
        try 
        { 
            var updated = await _brands.UpdateAsync(id, dto);
            try { await _audit.LogAsync("UpdateBrand", "Brand", id.ToString(), $"Updated brand {dto.NameAr}", System.Security.Claims.ClaimTypes.NameIdentifier, System.Security.Claims.ClaimTypes.Name); } catch { }
            return Ok(updated); 
        }
        catch (KeyNotFoundException) 
        { 
            return NotFound(); 
        }
        catch (ArgumentException ex) 
        { 
            return BadRequest(new { message = ex.Message }); 
        }
    }

    [RequirePermission(ModuleKeys.Brands, requireEdit: true)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try 
        { 
            await _brands.DeleteAsync(id); 
            try { await _audit.LogAsync("DeleteBrand", "Brand", id.ToString(), $"Deleted brand", System.Security.Claims.ClaimTypes.NameIdentifier, System.Security.Claims.ClaimTypes.Name); } catch { }
            return NoContent(); 
        }
        catch (KeyNotFoundException) 
        { 
            return NotFound(); 
        }
    }
}

