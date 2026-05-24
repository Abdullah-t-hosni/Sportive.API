using Sportive.API.Models;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CustomerCategoryController : ControllerBase
{
    private readonly ICustomerCategoryService _service;

    public CustomerCategoryController(ICustomerCategoryService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<List<CustomerCategoryDto>>> GetAll()
    {
        return Ok(await _service.GetAllAsync());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CustomerCategoryDto>> GetById(int id)
    {
        var category = await _service.GetByIdAsync(id);
        if (category == null) return NotFound();
        return Ok(category);
    }

    [HttpPost]
    [RequirePermission(ModuleKeys.Customers, requireEdit: true)]
    public async Task<ActionResult<CustomerCategoryDto>> Create(CreateCustomerCategoryDto dto)
    {
        var category = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = category.Id }, category);
    }

    [HttpPut("{id}")]
    [RequirePermission(ModuleKeys.Customers, requireEdit: true)]
    public async Task<ActionResult<CustomerCategoryDto>> Update(int id, UpdateCustomerCategoryDto dto)
    {
        try
        {
            var category = await _service.UpdateAsync(id, dto);
            return Ok(category);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id}")]
    [RequirePermission(ModuleKeys.Customers, requireEdit: true)]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _service.DeleteAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

