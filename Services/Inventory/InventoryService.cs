using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Utils;
using Microsoft.AspNetCore.SignalR;
using Sportive.API.Hubs;

namespace Sportive.API.Services;

public class InventoryService : IInventoryService
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;
    private readonly ITranslator _t;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly Sportive.API.Interfaces.ITenantContext _tenantContext;

    public InventoryService(AppDbContext db, INotificationService notifications, ITranslator t, IServiceScopeFactory scopeFactory, IHubContext<NotificationHub> hubContext, Sportive.API.Interfaces.ITenantContext tenantContext)
    {
        _db = db;
        _notifications = notifications;
        _t = t;
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _tenantContext = tenantContext;
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
        bool autoSave = true,
        bool broadcast = true,
        bool force = false,
        DateTime? date = null,
        bool ignoreIdempotency = false,
        int? warehouseId = null)
    {
        using var activity = Sportive.API.Utils.Telemetry.ActivitySource.StartActivity("Inventory Adjustment");
        if (activity != null)
        {
            activity.SetTag("movement.type", type.ToString());
            activity.SetTag("movement.quantity", quantity.ToString());
            activity.SetTag("product.id", productId?.ToString());
            activity.SetTag("reference", reference);
        }

        if (quantity == 0) return;
        if (productId == 0) productId = null;
        if (variantId == 0) variantId = null;

        int roundedQty = (int)Math.Round(quantity, MidpointRounding.AwayFromZero);
        if (roundedQty == 0 && quantity != 0) roundedQty = quantity > 0 ? 1 : -1;
        
        if (roundedQty == 0 && quantity == 0) return;

        // Idempotency check: prevent duplicate movements for the same order/invoice and item
        // Skip check if it's an opening balance to speed up bulk imports
        if (!ignoreIdempotency && !string.IsNullOrEmpty(reference) && type != InventoryMovementType.OpeningBalance)
        {
            bool exists = await _db.InventoryMovements.AnyAsync(m => 
                m.Type == type && 
                m.Reference == reference && 
                m.ProductId == productId && 
                m.ProductVariantId == variantId);
                
            if (exists) return;
        }

        int remainingBefore = 0;
        int newStock = 0;

        // Resolve warehouseId first
        int? warehouseIdToUse = warehouseId;
        if (!warehouseIdToUse.HasValue)
        {
            var defaultWarehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.Name == "المخزن الرئيسي" || w.IsActive);
            if (defaultWarehouse != null)
            {
                warehouseIdToUse = defaultWarehouse.Id;
            }
        }

        // 1. Update Actual Stock levels in Product/Variant
        if (variantId.HasValue)
        {
            var variant = await _db.ProductVariants.Include(v => v.Product).FirstOrDefaultAsync(v => v.Id == variantId);
            if (variant != null)
            {
                remainingBefore = variant.StockQuantity;
                if (!force && variant.StockQuantity + roundedQty < 0) 
                    throw new InvalidOperationException(_t.Get("Inventory.InsufficientStock", variant.StockQuantity, -roundedQty));

                // Dual-write: Update ProductWarehouseStock as well
                if (warehouseIdToUse.HasValue)
                {
                    var warehouseStock = await _db.ProductWarehouseStocks
                        .FirstOrDefaultAsync(pws => pws.ProductVariantId == variantId.Value && pws.WarehouseId == warehouseIdToUse.Value);
                    if (warehouseStock == null)
                    {
                        warehouseStock = new ProductWarehouseStock
                        {
                            ProductVariantId = variantId.Value,
                            WarehouseId = warehouseIdToUse.Value,
                            Quantity = 0,
                            CreatedAt = TimeHelper.GetEgyptTime()
                        };
                        _db.ProductWarehouseStocks.Add(warehouseStock);
                    }

                    if (!force && warehouseStock.Quantity + roundedQty < 0)
                        throw new InvalidOperationException(_t.Get("Inventory.InsufficientStockInWarehouse", warehouseStock.Quantity, -roundedQty));

                    warehouseStock.Quantity += roundedQty;
                    warehouseStock.UpdatedAt = TimeHelper.GetEgyptTime();
                }
                
                variant.StockQuantity += roundedQty;
                newStock = variant.StockQuantity;
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
                if (!force && product.TotalStock + roundedQty < 0) 
                    throw new InvalidOperationException(_t.Get("Inventory.InsufficientStock", product.TotalStock, -roundedQty));
                
                product.TotalStock += roundedQty;
                newStock = product.TotalStock;

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

        // 2. Cost Analysis & FIFO Logic
        var effectiveUnitCost = unitCost;
        int? remainingQtyForNewMove = null;

        if (roundedQty > 0)
        {
            // Receipt: Initialize RemainingQty for FIFO
            remainingQtyForNewMove = roundedQty;
            
            if (effectiveUnitCost <= 0 && productId.HasValue)
            {
                var p = await _db.Products.FindAsync(productId.Value);
                effectiveUnitCost = p?.CostPrice ?? 0;
            }
        }
        else if (roundedQty < 0)
        {
            // Sale/Adjustment Out: FIFO Consumption
            var toConsume = -roundedQty;
            decimal totalCogs = 0;
            var consumedCount = 0;

            // Get oldest receipts with remaining stock
            var layers = await _db.InventoryMovements
                .Where(m => m.ProductId == productId && 
                            m.ProductVariantId == variantId && 
                            m.RemainingQty > 0)
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Id)
                .ToListAsync();

            foreach (var layer in layers)
            {
                if (toConsume <= 0) break;

                var take = Math.Min(toConsume, layer.RemainingQty ?? 0);
                layer.RemainingQty -= take;
                toConsume -= take;
                totalCogs += take * layer.UnitCost;
                consumedCount += take;
            }

            // Calculate effective unit cost for this sale
            if (consumedCount > 0)
            {
                effectiveUnitCost = totalCogs / consumedCount;
            }
            else if (productId.HasValue)
            {
                // Fallback to product cost if no layers found (e.g. initial stock without layers)
                var p = await _db.Products.FindAsync(productId.Value);
                effectiveUnitCost = p?.CostPrice ?? 0;
            }
        }

        // 3. Create Movement Record
        _db.InventoryMovements.Add(new InventoryMovement
        {
            Type             = type,
            Quantity         = roundedQty,
            RemainingStock   = remainingBefore + roundedQty,
            RemainingQty     = remainingQtyForNewMove,
            ProductId        = productId,
            ProductVariantId = variantId,
            Reference        = reference,
            Note             = note,
            UnitCost         = effectiveUnitCost,
            CreatedByUserId  = userId,
            CostCenter       = costCenter,
            WarehouseId      = warehouseIdToUse,
            CreatedAt        = date ?? TimeHelper.GetEgyptTime()
        });

        if (autoSave) await _db.SaveChangesAsync();
        
        // 🚀 BROADCAST STOCK UPDATE: Notify all clients to update their UI
        if (broadcast && productId.HasValue)
        {
            var prefix = _tenantContext.CurrentTenant?.Slug?.ToLowerInvariant() ?? "global";
            await _hubContext.Clients.Group($"{prefix}_All").SendAsync("StockUpdated", new {
                productId = productId.Value,
                variantId = variantId ?? 0,
                newStock = newStock
            });
        }

        // 3. Low-stock alert & Cache Invalidation — Fire-and-forget to avoid blocking the main thread
        _ = Task.Run(async () => {
            try {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
                var t = scope.ServiceProvider.GetRequiredService<ITranslator>();

                // Invalidate operational reports cache
                var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
                await cache.RemoveByPrefixAsync("Inventory_");
                await cache.RemoveByPrefixAsync("ProdMovement_");
                if (type == InventoryMovementType.Sale || type == InventoryMovementType.ReturnIn)
                {
                    await cache.RemoveByPrefixAsync("Sales_");
                }
                else if (type == InventoryMovementType.Purchase || type == InventoryMovementType.ReturnOut)
                {
                    await cache.RemoveByPrefixAsync("Purchases_");
                }

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
