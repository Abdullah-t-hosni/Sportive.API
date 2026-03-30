using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BrandController : ControllerBase
{
    private readonly ICategoryService _categories;
    
    public BrandController(ICategoryService categories) 
    {
        _categories = categories;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var all = await _categories.GetAllAsync();
        return Ok(all.Where(x => x.Type == CategoryType.Brand.ToString()));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var cat = await _categories.GetByIdAsync(id);
        if (cat == null || cat.Type != CategoryType.Brand.ToString()) 
            return NotFound();
            
        return Ok(cat);
    }

    [Authorize(Roles = "Admin,Manager")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryDto dto)
    {
        try
        {
            var brandDto = dto with { Type = CategoryType.Brand };
            var cat = await _categories.CreateAsync(brandDto);
            return CreatedAtAction(nameof(GetById), new { id = cat.Id }, cat);
        }
        catch (ArgumentException ex) 
        { 
            return BadRequest(new { message = ex.Message }); 
        }
    }

    [Authorize(Roles = "Admin,Manager")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateCategoryDto dto)
    {
        try 
        { 
            var brandDto = dto with { Type = CategoryType.Brand };
            var cat = await _categories.UpdateAsync(id, brandDto);
            return Ok(cat); 
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

    [Authorize(Roles = "Admin,Manager")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try 
        { 
            var cat = await _categories.GetByIdAsync(id);
            if(cat == null || cat.Type != CategoryType.Brand.ToString()) 
                return NotFound();
            
            await _categories.DeleteAsync(id); 
            return NoContent(); 
        }
        catch (KeyNotFoundException) 
        { 
            return NotFound(); 
        }
    }
}
