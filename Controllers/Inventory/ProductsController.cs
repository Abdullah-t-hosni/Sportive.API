using System.Security.Claims;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Services;
using System.Security.Claims;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _products;
    private readonly IAuditService _audit;
    public ProductsController(IProductService products, IAuditService audit)
    {
        _products = products;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] ProductFilterDto filter) =>
        Ok(await _products.GetProductsAsync(filter));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id, [FromQuery] DiscountApplyTo? source = null, [FromQuery] int? warehouseId = null)
    {
        var product = await _products.GetProductByIdAsync(id, source, warehouseId);
        return product == null ? NotFound() : Ok(product);
    }

    [HttpGet("slug/{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, [FromQuery] DiscountApplyTo? source = null, [FromQuery] int? warehouseId = null)
    {
        if (int.TryParse(slug, out int id))
        {
            var productById = await _products.GetProductByIdAsync(id, source, warehouseId);
            if (productById != null) return Ok(productById);
        }
        var product = await _products.GetProductBySlugAsync(slug, source, warehouseId);
        return product == null ? NotFound() : Ok(product);
    }

    [HttpGet("featured")]
    public async Task<IActionResult> GetFeatured([FromQuery] int count = 8, [FromQuery] int? warehouseId = null) =>
        Ok(await _products.GetFeaturedProductsAsync(count, warehouseId));

    [HttpGet("{id}/related")]
    public async Task<IActionResult> GetRelated(int id, [FromQuery] int count = 4, [FromQuery] int? warehouseId = null) =>
        Ok(await _products.GetRelatedProductsAsync(id, count, warehouseId));

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductDto dto)
    {
        var product = await _products.CreateProductAsync(dto);
        try { await _audit.LogAsync("CreateProduct", "Product", product.Id.ToString(), $"Created product: {product.NameEn}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProductDto dto)
    {
        try { 
            var result = await _products.UpdateProductAsync(id, dto);
            try { await _audit.LogAsync("UpdateProduct", "Product", id.ToString(), $"Updated product", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
            return Ok(result); 
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try { 
            await _products.DeleteProductAsync(id); 
            try { await _audit.LogAsync("DeleteProduct", "Product", id.ToString(), $"Deleted product", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
            return NoContent(); 
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPatch("{id}/cost")]
    public async Task<IActionResult> UpdateCost(int id, [FromBody] decimal? costPrice)
    {
        var success = await _products.UpdateCostPriceAsync(id, costPrice);
        if (success) { try { await _audit.LogAsync("UpdateProductCost", "Product", id.ToString(), $"Updated cost price to {costPrice}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { } return Ok(); }
        return NotFound();
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPatch("{id}/size-chart")]
    public async Task<IActionResult> UpdateSizeChart(int id, [FromBody] UpdateSizeChartDto dto)
    {
        var success = await _products.UpdateSizeChartAsync(id, dto.SizeChartJson, dto.SizeChartImageUrl);
        if (success) { try { await _audit.LogAsync("UpdateSizeChart", "Product", id.ToString(), $"Updated size chart", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { } return Ok(); }
        return NotFound();
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPatch("variants/{variantId}/stock")]
    public async Task<IActionResult> UpdateStock(int variantId, [FromBody] int quantity)
    {
        var success = await _products.UpdateStockAsync(variantId, quantity);
        if (success) { try { await _audit.LogAsync("UpdateVariantStock", "ProductVariant", variantId.ToString(), $"Updated stock by {quantity}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { } return Ok(); }
        return NotFound();
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPatch("{id}/stock")]
    public async Task<IActionResult> UpdateProductStock(int id, [FromBody] int quantity)
    {
        var success = await _products.UpdateProductStockAsync(id, quantity);
        if (success) { try { await _audit.LogAsync("UpdateProductStock", "Product", id.ToString(), $"Updated base stock by {quantity}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { } return Ok(); }
        return NotFound();
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPost("{productId}/variants")]
    public async Task<IActionResult> AddVariant(int productId, [FromBody] CreateVariantDto dto)
    {
        var variant = await _products.AddVariantAsync(productId, dto);
        try { await _audit.LogAsync("AddVariant", "ProductVariant", variant.Id.ToString(), $"Added variant to product {productId}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return Ok(variant);
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPatch("variants/{variantId}")]
    public async Task<IActionResult> UpdateVariant(int variantId, [FromBody] CreateVariantDto dto)
    {
        var variant = await _products.UpdateVariantAsync(variantId, dto);
        try { await _audit.LogAsync("UpdateVariant", "ProductVariant", variantId.ToString(), $"Updated variant", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return Ok(variant);
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpDelete("variants/{variantId}")]
    public async Task<IActionResult> DeleteVariant(int variantId)
    {
        var success = await _products.DeleteVariantAsync(variantId);
        if (success) { try { await _audit.LogAsync("DeleteVariant", "ProductVariant", variantId.ToString(), $"Deleted variant", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { } return NoContent(); }
        return NotFound();
    }
}

