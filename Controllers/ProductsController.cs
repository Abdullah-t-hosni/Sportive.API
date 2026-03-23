using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _products;
    public ProductsController(IProductService products) => _products = products;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] ProductFilterDto filter) =>
        Ok(await _products.GetProductsAsync(filter));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var product = await _products.GetProductByIdAsync(id);
        return product == null ? NotFound() : Ok(product);
    }

    [HttpGet("featured")]
    public async Task<IActionResult> GetFeatured([FromQuery] int count = 8) =>
        Ok(await _products.GetFeaturedProductsAsync(count));

    [HttpGet("{id}/related")]
    public async Task<IActionResult> GetRelated(int id, [FromQuery] int count = 4) =>
        Ok(await _products.GetRelatedProductsAsync(id, count));

    [Authorize(Roles = "Admin,Staff")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductDto dto)
    {
        var product = await _products.CreateProductAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProductDto dto)
    {
        try { return Ok(await _products.UpdateProductAsync(id, dto)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try { await _products.DeleteProductAsync(id); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpPatch("variants/{variantId}/stock")]
    public async Task<IActionResult> UpdateStock(int variantId, [FromBody] int quantity) =>
        await _products.UpdateStockAsync(variantId, quantity) ? Ok() : NotFound();
}
