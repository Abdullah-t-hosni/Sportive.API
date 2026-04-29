using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

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
    public async Task<IActionResult> GetById(int id, [FromQuery] DiscountApplyTo? source = null)
    {
        var product = await _products.GetProductByIdAsync(id, source);
        return product == null ? NotFound() : Ok(product);
    }

    [HttpGet("slug/{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, [FromQuery] DiscountApplyTo? source = null)
    {
        var product = await _products.GetProductBySlugAsync(slug, source);
        return product == null ? NotFound() : Ok(product);
    }

    [HttpGet("featured")]
    public async Task<IActionResult> GetFeatured([FromQuery] int count = 8) =>
        Ok(await _products.GetFeaturedProductsAsync(count));

    [HttpGet("{id}/related")]
    public async Task<IActionResult> GetRelated(int id, [FromQuery] int count = 4) =>
        Ok(await _products.GetRelatedProductsAsync(id, count));

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductDto dto)
    {
        var product = await _products.CreateProductAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProductDto dto)
    {
        try { return Ok(await _products.UpdateProductAsync(id, dto)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try { await _products.DeleteProductAsync(id); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPatch("{id}/cost")]
    public async Task<IActionResult> UpdateCost(int id, [FromBody] decimal? costPrice) =>
        await _products.UpdateCostPriceAsync(id, costPrice) ? Ok() : NotFound();

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPatch("variants/{variantId}/stock")]
    public async Task<IActionResult> UpdateStock(int variantId, [FromBody] int quantity) =>
        await _products.UpdateStockAsync(variantId, quantity) ? Ok() : NotFound();

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPatch("{id}/stock")]
    public async Task<IActionResult> UpdateProductStock(int id, [FromBody] int quantity) =>
        await _products.UpdateProductStockAsync(id, quantity) ? Ok() : NotFound();

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPost("{productId}/variants")]
    public async Task<IActionResult> AddVariant(int productId, [FromBody] CreateVariantDto dto) =>
        Ok(await _products.AddVariantAsync(productId, dto));

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPatch("variants/{variantId}")]
    public async Task<IActionResult> UpdateVariant(int variantId, [FromBody] CreateVariantDto dto) =>
        Ok(await _products.UpdateVariantAsync(variantId, dto));

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpDelete("variants/{variantId}")]
    public async Task<IActionResult> DeleteVariant(int variantId) =>
        await _products.DeleteVariantAsync(variantId) ? NoContent() : NotFound();
}

