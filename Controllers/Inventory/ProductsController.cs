using System.Security.Claims;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Services;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _products;
    private readonly IAuditService _audit;
    private readonly AppDbContext _db;
    public ProductsController(IProductService products, IAuditService audit, AppDbContext db)
    {
        _products = products;
        _audit = audit;
        _db = db;
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
        var newProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == product.Id);
        try { await _audit.LogChangeAsync<Product>("CreateProduct", "Product", product.Id.ToString(), null, newProduct, User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProductDto dto)
    {
        try { 
            var oldProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            var result = await _products.UpdateProductAsync(id, dto);
            var newProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            try { await _audit.LogChangeAsync<Product>("UpdateProduct", "Product", id.ToString(), oldProduct, newProduct, User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
            return Ok(result); 
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try { 
            var oldProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            await _products.DeleteProductAsync(id); 
            try { await _audit.LogChangeAsync<Product>("DeleteProduct", "Product", id.ToString(), oldProduct, null, User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
            return NoContent(); 
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPatch("{id}/cost")]
    public async Task<IActionResult> UpdateCost(int id, [FromBody] decimal? costPrice)
    {
        var oldProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        var success = await _products.UpdateCostPriceAsync(id, costPrice);
        if (success) { 
            var newProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            try { await _audit.LogChangeAsync<Product>("UpdateProductCost", "Product", id.ToString(), oldProduct, newProduct, User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { } 
            return Ok(); 
        }
        return NotFound();
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPatch("{id}/size-chart")]
    public async Task<IActionResult> UpdateSizeChart(int id, [FromBody] UpdateSizeChartDto dto)
    {
        var oldProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        var success = await _products.UpdateSizeChartAsync(id, dto.SizeChartJson, dto.SizeChartImageUrl);
        if (success) { 
            var newProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            try { await _audit.LogChangeAsync<Product>("UpdateSizeChart", "Product", id.ToString(), oldProduct, newProduct, User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { } 
            return Ok(); 
        }
        return NotFound();
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPatch("variants/{variantId}/stock")]
    public async Task<IActionResult> UpdateStock(int variantId, [FromBody] int quantity)
    {
        var oldVariant = await _db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == variantId);
        var success = await _products.UpdateStockAsync(variantId, quantity);
        if (success) { 
            var newVariant = await _db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == variantId);
            try { await _audit.LogChangeAsync<ProductVariant>("UpdateVariantStock", "ProductVariant", variantId.ToString(), oldVariant, newVariant, User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { } 
            return Ok(); 
        }
        return NotFound();
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPatch("{id}/stock")]
    public async Task<IActionResult> UpdateProductStock(int id, [FromBody] int quantity)
    {
        var oldProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        var success = await _products.UpdateProductStockAsync(id, quantity);
        if (success) { 
            var newProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            try { await _audit.LogChangeAsync<Product>("UpdateProductStock", "Product", id.ToString(), oldProduct, newProduct, User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { } 
            return Ok(); 
        }
        return NotFound();
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPost("{productId}/variants")]
    public async Task<IActionResult> AddVariant(int productId, [FromBody] CreateVariantDto dto)
    {
        var variant = await _products.AddVariantAsync(productId, dto);
        var newVariant = await _db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == variant.Id);
        try { await _audit.LogChangeAsync<ProductVariant>("AddVariant", "ProductVariant", variant.Id.ToString(), null, newVariant, User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return Ok(variant);
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpPatch("variants/{variantId}")]
    public async Task<IActionResult> UpdateVariant(int variantId, [FromBody] CreateVariantDto dto)
    {
        var oldVariant = await _db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == variantId);
        var variant = await _products.UpdateVariantAsync(variantId, dto);
        var newVariant = await _db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == variantId);
        try { await _audit.LogChangeAsync<ProductVariant>("UpdateVariant", "ProductVariant", variantId.ToString(), oldVariant, newVariant, User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }
        return Ok(variant);
    }

    [RequirePermission(ModuleKeys.Products, requireEdit: true)]
    [HttpDelete("variants/{variantId}")]
    public async Task<IActionResult> DeleteVariant(int variantId)
    {
        var oldVariant = await _db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == variantId);
        var success = await _products.DeleteVariantAsync(variantId);
        if (success) { 
            try { await _audit.LogChangeAsync<ProductVariant>("DeleteVariant", "ProductVariant", variantId.ToString(), oldVariant, null, User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { } 
            return NoContent(); 
        }
        return NotFound();
    }

    [HttpPost("fix-warehouse-stock-discrepancy/{productId}")]
    [AllowAnonymous]
    public async Task<IActionResult> FixStockDiscrepancy(int productId)
    {
        var product = await _db.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null) return NotFound("Product not found.");

        var defaultWarehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.IsActive);
        if (defaultWarehouse == null) return BadRequest("No active warehouse found.");

        int fixedVariants = 0;
        foreach (var variant in product.Variants)
        {
            var warehouseStocks = await _db.ProductWarehouseStocks
                .Where(w => w.ProductId == productId && w.ProductVariantId == variant.Id)
                .ToListAsync();
            
            var totalWarehouseStock = warehouseStocks.Sum(w => w.Quantity);
            var diff = variant.StockQuantity - totalWarehouseStock;

            if (diff != 0)
            {
                var ws = warehouseStocks.FirstOrDefault(w => w.WarehouseId == defaultWarehouse.Id);
                if (ws != null)
                {
                    ws.Quantity += diff;
                }
                else
                {
                    _db.ProductWarehouseStocks.Add(new ProductWarehouseStock
                    {
                        WarehouseId = defaultWarehouse.Id,
                        ProductId = productId,
                        ProductVariantId = variant.Id,
                        Quantity = diff, // assign the missing amount here
                        CreatedAt = Sportive.API.Utils.TimeHelper.GetEgyptTime()
                    });
                }
                fixedVariants++;
            }
        }
        
        await _db.SaveChangesAsync();
        return Ok(new { message = $"Discrepancy fixed successfully. {fixedVariants} variants updated.", defaultWarehouseId = defaultWarehouse.Id });
    }
}

