using Sportive.API.Models;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BrandController : ControllerBase
{
    private readonly IBrandService _brands;
    
    public BrandController(IBrandService brands) 
    {
        _brands = brands;
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
            return NoContent(); 
        }
        catch (KeyNotFoundException) 
        { 
            return NotFound(); 
        }
    }
}

