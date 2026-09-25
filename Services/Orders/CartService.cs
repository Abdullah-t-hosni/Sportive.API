using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public class CartService : ICartService
{
    private readonly AppDbContext _db;
    public CartService(AppDbContext db) => _db = db;

    public async Task<CartSummaryDto> GetCartAsync(int customerId)
    {
        var items = await GetCartItemsAsync(customerId);
        var store = await _db.StoreInfo.AsNoTracking().FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        
        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var now = Utils.TimeHelper.GetEgyptTime();
        var discounts = await _db.ProductDiscounts
            .AsNoTracking()
            .Where(d => d.IsActive && d.ValidFrom <= now && d.ValidTo >= now)
            .Where(d => d.ApplyTo == DiscountApplyTo.All || d.ApplyTo == DiscountApplyTo.Store)
            .ToListAsync();

        var allCategories = await _db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.ParentId);

        return BuildSummary(items, store, discounts, allCategories);
    }

    public async Task<CartSummaryDto> AddToCartAsync(int customerId, AddToCartDto dto)
    {
        var product = await _db.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == dto.ProductId);
        if (product == null) throw new KeyNotFoundException("المنتج غير موجود");

        if (product.Variants.Any(v => v.IsActive) && !dto.ProductVariantId.HasValue)
        {
            throw new ArgumentException($"يجب تحديد المقاس واللون للمنتج '{product.NameAr}' لإضافته للسلة.");
        }

        var store = await _db.StoreInfo.AsNoTracking().FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        var variant = dto.ProductVariantId.HasValue ? product.Variants.FirstOrDefault(v => v.Id == dto.ProductVariantId.Value) : null;
        var rawStock = variant?.StockQuantity ?? product.TotalStock;
        var availableStock = (variant?.MaxOnlineStock != null && variant.MaxOnlineStock.Value > 0)
            ? Math.Min(rawStock, variant.MaxOnlineStock.Value)
            : rawStock;

        var existing = await _db.CartItems.FirstOrDefaultAsync(c =>
            c.CustomerId == customerId &&
            c.ProductId == dto.ProductId &&
            c.ProductVariantId == dto.ProductVariantId);

        int requestedQty = (existing?.Quantity ?? 0) + dto.Quantity;
        if (store != null && !store.AllowBackorders && requestedQty > availableStock)
        {
            throw new ArgumentException($"الكمية المطلوبة ({requestedQty}) تتجاوز المخزون المتوفر ({availableStock}) للمنتج '{product.NameAr}'.");
        }

        if (existing != null)
            existing.Quantity = requestedQty;
        else
            _db.CartItems.Add(new CartItem
            {
                CustomerId = customerId,
                ProductId = dto.ProductId,
                ProductVariantId = dto.ProductVariantId,
                Quantity = dto.Quantity
            });

        // Reset abandoned cart fields if it was previously recovered
        var customer = await _db.Customers.FindAsync(customerId);
        if (customer != null && customer.IsAbandonedCartRecovered)
        {
            customer.IsAbandonedCartRecovered = false;
            customer.AbandonedCartRecoveredAt = null;
            customer.AbandonedCartRecoveredOrderNumber = null;
            customer.AbandonedCartReminderSentAt = null;
            customer.AbandonedCartCouponCode = null;
            customer.AbandonedCartValue = null;
        }

        await _db.SaveChangesAsync();
        return await GetCartAsync(customerId);
    }

    public async Task<CartSummaryDto> BulkAddToCartAsync(int customerId, BulkAddToCartDto dto)
    {
        if (dto.Items == null || !dto.Items.Any()) return await GetCartAsync(customerId);

        var store = await _db.StoreInfo.AsNoTracking().FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        var productIds = dto.Items.Select(i => i.ProductId).Distinct().ToList();
        var productsDict = await _db.Products.Include(p => p.Variants).Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        foreach (var item in dto.Items)
        {
            if (!productsDict.TryGetValue(item.ProductId, out var product)) continue;

            if (product.Variants.Any(v => v.IsActive) && !item.ProductVariantId.HasValue)
            {
                throw new ArgumentException($"يجب تحديد المقاس واللون للمنتج '{product.NameAr}' لإضافته للسلة.");
            }

            var variant = item.ProductVariantId.HasValue ? product.Variants.FirstOrDefault(v => v.Id == item.ProductVariantId.Value) : null;
            var rawStock = variant?.StockQuantity ?? product.TotalStock;
            var availableStock = (variant?.MaxOnlineStock != null && variant.MaxOnlineStock.Value > 0)
                ? Math.Min(rawStock, variant.MaxOnlineStock.Value)
                : rawStock;

            var existing = await _db.CartItems.FirstOrDefaultAsync(c =>
                c.CustomerId == customerId &&
                c.ProductId == item.ProductId &&
                c.ProductVariantId == item.ProductVariantId);

            int requestedQty = (existing?.Quantity ?? 0) + item.Quantity;
            if (store != null && !store.AllowBackorders && requestedQty > availableStock)
            {
                throw new ArgumentException($"الكمية المطلوبة ({requestedQty}) تتجاوز المخزون المتوفر ({availableStock}) للمنتج '{product.NameAr}'.");
            }

            if (existing != null)
                existing.Quantity = requestedQty;
            else
                _db.CartItems.Add(new CartItem
                {
                    CustomerId = customerId,
                    ProductId = item.ProductId,
                    ProductVariantId = item.ProductVariantId,
                    Quantity = item.Quantity
                });
        }

        // Reset abandoned cart fields if it was previously recovered
        var customer = await _db.Customers.FindAsync(customerId);
        if (customer != null && customer.IsAbandonedCartRecovered)
        {
            customer.IsAbandonedCartRecovered = false;
            customer.AbandonedCartRecoveredAt = null;
            customer.AbandonedCartRecoveredOrderNumber = null;
            customer.AbandonedCartReminderSentAt = null;
            customer.AbandonedCartCouponCode = null;
            customer.AbandonedCartValue = null;
        }

        await _db.SaveChangesAsync();
        return await GetCartAsync(customerId);
    }

    public async Task<CartSummaryDto> UpdateCartItemAsync(int customerId, int cartItemId, UpdateCartItemDto dto)
    {
        var item = await _db.CartItems
            .FirstOrDefaultAsync(c => c.Id == cartItemId && c.CustomerId == customerId)
            ?? throw new KeyNotFoundException("Cart item not found");

        var product = await _db.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == item.ProductId);
        if (product != null)
        {
            var store = await _db.StoreInfo.AsNoTracking().FirstOrDefaultAsync(s => s.StoreConfigId == 1);
            var variant = item.ProductVariantId.HasValue ? product.Variants.FirstOrDefault(v => v.Id == item.ProductVariantId.Value) : null;
            var rawStock = variant?.StockQuantity ?? product.TotalStock;
            var availableStock = (variant?.MaxOnlineStock != null && variant.MaxOnlineStock.Value > 0)
                ? Math.Min(rawStock, variant.MaxOnlineStock.Value)
                : rawStock;

            if (store != null && !store.AllowBackorders && dto.Quantity > availableStock)
            {
                throw new ArgumentException($"الكمية المطلوبة ({dto.Quantity}) تتجاوز المخزون المتوفر ({availableStock}) للمنتج '{product.NameAr}'.");
            }
        }

        item.Quantity = dto.Quantity;
        await _db.SaveChangesAsync();
        return await GetCartAsync(customerId);
    }

    public async Task<CartSummaryDto> RemoveFromCartAsync(int customerId, int cartItemId)
    {
        var item = await _db.CartItems
            .FirstOrDefaultAsync(c => c.Id == cartItemId && c.CustomerId == customerId)
            ?? throw new KeyNotFoundException("Cart item not found");

        _db.CartItems.Remove(item);
        await _db.SaveChangesAsync();
        return await GetCartAsync(customerId);
    }

    public async Task ClearCartAsync(int customerId)
    {
        var items = await _db.CartItems.Where(c => c.CustomerId == customerId).ToListAsync();
        _db.CartItems.RemoveRange(items);
        await _db.SaveChangesAsync();
    }

    private async Task<List<CartItem>> GetCartItemsAsync(int customerId) =>
        await _db.CartItems
            .AsNoTracking()
            .Include(c => c.Product).ThenInclude(p => p!.Images)
            .Include(c => c.ProductVariant)
            .Where(c => c.CustomerId == customerId)
            .ToListAsync();

    private static CartSummaryDto BuildSummary(List<CartItem> items, StoreInfo? store, List<ProductDiscount> discounts, Dictionary<int, int?> allCategories)
    {
        var deliveryFee = store?.FixedDeliveryFee ?? 50m;

        // 🎁 DETECT BUNDLE OFFERS IN CART
        var cartProductIds = items.Where(c => c.Product != null && c.ProductId.HasValue).Select(c => c.ProductId!.Value).Distinct().ToHashSet();
        var cartBundlePctDiscounts = new Dictionary<int, decimal>();
        decimal cartBundleFixedDiscountTotal = 0;

        foreach (var ci in items)
        {
            if (ci.Product != null && !string.IsNullOrWhiteSpace(ci.Product.BundleProductIds) && ci.Product.BundleDiscountType > 0 && ci.Product.BundleDiscountValue > 0)
            {
                var bConfigs = ProductService.ParseBundleConfigs(ci.Product.BundleProductIds, ci.Product.LinkedProductId);
                if (bConfigs.Any() && bConfigs.All(cfg => items.Where(x => x.ProductId == cfg.ProductId).Sum(x => x.Quantity) >= cfg.Quantity))
                {
                    if (ci.Product.BundleDiscountType == 1) // Percentage
                    {
                        var allGroup = bConfigs.Select(c => c.ProductId).Append(ci.Product.Id).Distinct();
                        foreach (var gid in allGroup)
                        {
                            if (!cartBundlePctDiscounts.ContainsKey(gid))
                                cartBundlePctDiscounts[gid] = ci.Product.BundleDiscountValue;
                        }
                    }
                    else if (ci.Product.BundleDiscountType == 2) // Fixed amount
                    {
                        cartBundleFixedDiscountTotal += ci.Product.BundleDiscountValue;
                    }
                }
            }
        }

        var dtos = items.Select(c =>
        {
            var basePrice = (c.Product?.OnlinePrice.HasValue == true && c.Product.OnlinePrice.Value > 0)
                ? c.Product.OnlinePrice.Value
                : (c.Product?.Price ?? 0);
            
            var disc = discounts.FirstOrDefault(d => d.ProductId == c.ProductId);
            if (disc == null)
            {
                int? currentCatId = c.Product?.CategoryId;
                while (currentCatId.HasValue && disc == null)
                {
                    int lookupId = currentCatId.Value;
                    disc = discounts.FirstOrDefault(d => d.CategoryId == lookupId);
                    if (disc == null)
                    {
                        currentCatId = allCategories.GetValueOrDefault(lookupId);
                    }
                }
            }
            if (disc == null) disc = discounts.FirstOrDefault(d => d.BrandId == c.Product?.BrandId);
            
            // Strict Offers Page Resolution: Product -> Category Tree -> Brand
            // Products ONLY receive discounts if they, their category, or their brand are explicitly targeted in the Offers Page.

            if (disc != null && IsCategoryExcluded(disc.ExcludedCategoryIds, c.Product?.CategoryId, allCategories))
            {
                disc = null;
            }

            decimal price;
            if (disc != null && c.Quantity >= disc.MinQty)
            {
                price = disc.DiscountType == DiscountType.Percentage 
                    ? Math.Round(basePrice - (basePrice * disc.DiscountValue / 100), 2)
                    : Math.Round(basePrice - disc.DiscountValue, 2);
            }
            else if (c.ProductId.HasValue && cartBundlePctDiscounts.TryGetValue(c.ProductId.Value, out var cbPct) && cbPct > 0)
            {
                price = Math.Round(basePrice - (basePrice * cbPct / 100), 2);
            }
            else if (c.Product?.OnlineDiscountPrice.HasValue == true && c.Product.OnlineDiscountPrice.Value > 0)
            {
                price = c.Product.OnlineDiscountPrice.Value;
            }
            else
            {
                price = (c.Product?.DiscountPrice > 0) ? c.Product.DiscountPrice.Value : basePrice;
            }

            var variantAdj = c.ProductVariant?.OnlinePriceAdjustment ?? c.ProductVariant?.PriceAdjustment ?? 0;
            price += variantAdj;

            return new CartItemDto(
                c.Id, c.ProductId, c.ProductVariantId,
                c.Product?.NameAr ?? "", c.Product?.NameEn ?? "",
                c.Product?.Images.FirstOrDefault(i => i.IsMain)?.ImageUrl,
                c.ProductVariant?.Size, c.ProductVariant?.Color, c.ProductVariant?.ColorAr,
                c.Quantity, price, price * c.Quantity
            );
        }).ToList();

        var subTotal = Math.Max(0, dtos.Sum(d => d.TotalPrice) - cartBundleFixedDiscountTotal);

        // Apply free-delivery threshold from store settings
        var freeAt = store?.FreeDeliveryAt ?? 2000m;
        var appliedFee = subTotal >= freeAt ? 0m : deliveryFee;

        return new CartSummaryDto(dtos, subTotal, appliedFee, subTotal + appliedFee, dtos.Sum(d => d.Quantity));
    }

    private static bool IsCategoryExcluded(string? excludedCategoryIds, int? productCatId, Dictionary<int, int?> allCategories)
    {
        if (string.IsNullOrWhiteSpace(excludedCategoryIds) || !productCatId.HasValue) return false;
        var ids = excludedCategoryIds.Split(new[] { ',', ';', ' ', '[', ']', '"' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(s => int.TryParse(s.Trim(), out int id) ? id : 0)
                                     .Where(id => id > 0)
                                     .ToHashSet();
        if (ids.Count == 0) return false;
        int? current = productCatId;
        while (current.HasValue)
        {
            if (ids.Contains(current.Value)) return true;
            current = allCategories.GetValueOrDefault(current.Value);
        }
        return false;
    }
}
