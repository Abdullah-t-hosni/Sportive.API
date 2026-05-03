using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services;

public class InventoryService : IInventoryService
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;
    private readonly ITranslator _t;
    private readonly IServiceScopeFactory _scopeFactory;

    public InventoryService(AppDbContext db, INotificationService notifications, ITranslator t, IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _notifications = notifications;
        _t = t;
        _scopeFactory = scopeFactory;
    }

    public async Task LogMovementAsync(
        InventoryMovementType type,
        decimal quantity,
        int? productId = null,
        int? variantId = null,
        string? reference = null,
        string? note = null,
        string? userId = null,
        decimal unitCost = 0,
        OrderSource? costCenter = null,
        bool autoSave = true)
    {
        if (quantity == 0) return;
        if (productId == 0) productId = null;
        if (variantId == 0) variantId = null;

        int roundedQty = (int)Math.Round(quantity, MidpointRounding.AwayFromZero);
        if (roundedQty == 0 && quantity != 0) roundedQty = quantity > 0 ? 1 : -1;
        
        if (roundedQty == 0 && quantity == 0) return;

        // Idempotency check: prevent duplicate movements for the same order/invoice and item
        if (!string.IsNullOrEmpty(reference))
        {
            bool exists = await _db.InventoryMovements.AnyAsync(m => 
                m.Type == type && 
                m.Reference == reference && 
                m.ProductId == productId && 
                m.ProductVariantId == variantId);
                
            if (exists) return;
        }

        int remainingBefore = 0;

        // 1. Update Actual Stock levels in Product/Variant
        if (variantId.HasValue)
        {
            var variant = await _db.ProductVariants.Include(v => v.Product).FirstOrDefaultAsync(v => v.Id == variantId);
            if (variant != null)
            {
                remainingBefore = variant.StockQuantity;
                if (variant.StockQuantity + roundedQty < 0) 
                    throw new InvalidOperationException(_t.Get("Inventory.InsufficientStock", variant.StockQuantity, -roundedQty));
                
                variant.StockQuantity += roundedQty;
                variant.UpdatedAt = TimeHelper.GetEgyptTime();

                // Sync parent product total stock — OPTIMIZED: Avoid Re-Summing everything
                variant.Product.TotalStock += roundedQty;
                
                // 💡 AUTO-STATUS: Active <-> OutOfStock based on physical stock
                if (variant.Product.Status == ProductStatus.Active && variant.Product.TotalStock <= 0)
                {
                    variant.Product.Status = ProductStatus.OutOfStock;
                }
                else if (variant.Product.Status == ProductStatus.OutOfStock && variant.Product.TotalStock > 0)
                {
                    variant.Product.Status = ProductStatus.Active;
                }

                variant.Product.UpdatedAt  = TimeHelper.GetEgyptTime();

                productId = variant.ProductId; // ensure we log the product id too
            }
        }
        else if (productId.HasValue)
        {
            var product = await _db.Products.FindAsync(productId);
            if (product != null)
            {
                remainingBefore = product.TotalStock;
                if (product.TotalStock + roundedQty < 0) 
                    throw new InvalidOperationException(_t.Get("Inventory.InsufficientStock", product.TotalStock, -roundedQty));
                
                product.TotalStock += roundedQty;

                // 💡 AUTO-STATUS: Active <-> OutOfStock based on physical stock
                if (product.Status == ProductStatus.Active && product.TotalStock <= 0)
                {
                    product.Status = ProductStatus.OutOfStock;
                }
                else if (product.Status == ProductStatus.OutOfStock && product.TotalStock > 0)
                {
                    product.Status = ProductStatus.Active;
                }

                product.UpdatedAt = TimeHelper.GetEgyptTime();
            }
        }

        // 2. Cost Analysis
        var effectiveUnitCost = unitCost;
        if (effectiveUnitCost <= 0 && productId.HasValue)
        {
            var p = await _db.Products.FindAsync(productId.Value);
            effectiveUnitCost = p?.CostPrice ?? 0;
        }

        // 3. Create Movement Record
        _db.InventoryMovements.Add(new InventoryMovement
        {
            Type             = type,
            Quantity         = roundedQty,
            RemainingStock   = remainingBefore + roundedQty,
            ProductId        = productId,
            ProductVariantId = variantId,
            Reference        = reference,
            Note             = note,
            UnitCost         = effectiveUnitCost,
            CreatedByUserId  = userId,
            CostCenter       = costCenter,
            CreatedAt        = TimeHelper.GetEgyptTime()
        });

        if (autoSave) await _db.SaveChangesAsync();

        // 3. Low-stock alert — Fire-and-forget to avoid blocking the main thread
        _ = Task.Run(async () => {
            try {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
                var t = scope.ServiceProvider.GetRequiredService<ITranslator>();

                if (variantId.HasValue)
                {
                    var v = await db.ProductVariants.Include(pv => pv.Product).FirstOrDefaultAsync(pv => pv.Id == variantId);
                    if (v != null)
                    {
                        var reorder = v.ReorderLevel > 0 ? v.ReorderLevel : v.Product.ReorderLevel;
                        if (reorder > 0 && v.StockQuantity <= reorder && v.StockQuantity >= 0)
                            await notifications.SendAsync(null, t.Get("Inventory.LowStockAlertTitle"), "Low Stock Alert", 
                                t.Get("Inventory.LowStockAlertDesc", v.Product.NameAr, v.StockQuantity, reorder),
                                $"Product \"{v.Product.NameEn}\" reached {v.StockQuantity} units", "Alert");
                    }
                }
                else if (productId.HasValue)
                {
                    var p = await db.Products.FindAsync(productId);
                    if (p != null && p.ReorderLevel > 0 && p.TotalStock <= p.ReorderLevel && p.TotalStock >= 0)
                        await notifications.SendAsync(null, t.Get("Inventory.LowStockAlertTitle"), "Low Stock Alert",
                            t.Get("Inventory.LowStockAlertDesc", p.NameAr, p.TotalStock, p.ReorderLevel),
                            $"Product \"{p.NameEn}\" reached {p.TotalStock} units", "Alert");
                }
            } catch { /* Suppress background errors */ }
        });
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
