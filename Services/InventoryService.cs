using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public class InventoryService : IInventoryService
{
    private readonly AppDbContext _db;
    public InventoryService(AppDbContext db) => _db = db;

    public async Task LogMovementAsync(
        InventoryMovementType type,
        int quantity,
        int? productId = null,
        int? variantId = null,
        string? reference = null,
        string? note = null,
        string? userId = null)
    {
        if (quantity == 0) return;

        decimal unitCost = 0;
        int remainingBefore = 0;

        // 1. Update Actual Stock levels in Product/Variant
        if (variantId.HasValue)
        {
            var variant = await _db.ProductVariants.Include(v => v.Product).FirstOrDefaultAsync(v => v.Id == variantId);
            if (variant != null)
            {
                unitCost = variant.Product.CostPrice ?? 0;
                remainingBefore = variant.StockQuantity;
                variant.StockQuantity += quantity;
                variant.UpdatedAt = DateTime.UtcNow;

                // Sync parent product total stock
                var totalStock = await _db.ProductVariants
                    .Where(v => v.ProductId == variant.ProductId)
                    .SumAsync(v => v.StockQuantity);
                
                variant.Product.TotalStock = totalStock;
                if (variant.Product.Status == ProductStatus.OutOfStock && totalStock > 0)
                    variant.Product.Status = ProductStatus.Active;
                variant.Product.UpdatedAt  = DateTime.UtcNow;

                productId = variant.ProductId; // ensure we log the product id too
            }
        }
        else if (productId.HasValue)
        {
            var product = await _db.Products.FindAsync(productId);
            if (product != null)
            {
                unitCost = product.CostPrice ?? 0;
                remainingBefore = product.TotalStock;
                product.TotalStock += quantity;
                if (product.Status == ProductStatus.OutOfStock && product.TotalStock > 0)
                    product.Status = ProductStatus.Active;
                product.UpdatedAt = DateTime.UtcNow;
            }
        }

        // 2. Create Movement Record
        _db.InventoryMovements.Add(new InventoryMovement
        {
            Type             = type,
            Quantity         = quantity,
            RemainingStock   = remainingBefore + quantity,
            ProductId        = productId,
            ProductVariantId = variantId,
            Reference        = reference,
            Note             = note,
            UnitCost         = unitCost,
            CreatedByUserId  = userId,
            CreatedAt        = DateTime.UtcNow
        });

        // We don't save changes here to allow the calling method to wrap it in its own transaction if needed
    }

    public async Task<List<InventoryMovement>> GetMovementsAsync(int? productId = null, int? variantId = null)
    {
        var q = _db.InventoryMovements.AsQueryable();
        if (variantId.HasValue) q = q.Where(m => m.ProductVariantId == variantId);
        else if (productId.HasValue) q = q.Where(m => m.ProductId == productId);

        return await q.OrderByDescending(m => m.CreatedAt).ToListAsync();
    }

    public async Task<int> GetCurrentStockAsync(int? productId = null, int? variantId = null)
    {
        if (variantId.HasValue)
            return await _db.ProductVariants.Where(v => v.Id == variantId).Select(v => v.StockQuantity).FirstOrDefaultAsync();
        
        if (productId.HasValue)
            return await _db.Products.Where(p => p.Id == productId).Select(p => p.TotalStock).FirstOrDefaultAsync();

        return 0;
    }
}
