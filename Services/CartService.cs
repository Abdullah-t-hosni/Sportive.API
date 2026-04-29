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
            .Where(d => productIds.Contains(d.ProductId) && d.IsActive && d.ValidFrom <= now && d.ValidTo >= now)
            .ToListAsync();

        return BuildSummary(items, store, discounts);
    }

    public async Task<CartSummaryDto> AddToCartAsync(int customerId, AddToCartDto dto)
    {
        var existing = await _db.CartItems.FirstOrDefaultAsync(c =>
            c.CustomerId == customerId &&
            c.ProductId == dto.ProductId &&
            c.ProductVariantId == dto.ProductVariantId);

        if (existing != null)
            existing.Quantity += dto.Quantity;
        else
            _db.CartItems.Add(new CartItem
            {
                CustomerId = customerId,
                ProductId = dto.ProductId,
                ProductVariantId = dto.ProductVariantId,
                Quantity = dto.Quantity
            });

        await _db.SaveChangesAsync();
        return await GetCartAsync(customerId);
    }

    public async Task<CartSummaryDto> UpdateCartItemAsync(int customerId, int cartItemId, UpdateCartItemDto dto)
    {
        var item = await _db.CartItems
            .FirstOrDefaultAsync(c => c.Id == cartItemId && c.CustomerId == customerId)
            ?? throw new KeyNotFoundException("Cart item not found");

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

    private static CartSummaryDto BuildSummary(List<CartItem> items, StoreInfo? store, List<ProductDiscount> discounts)
    {
        var deliveryFee = store?.FixedDeliveryFee ?? 50m;

        var dtos = items.Select(c =>
        {
            var basePrice = c.Product?.Price ?? 0;
            var disc = discounts.FirstOrDefault(d => d.ProductId == c.ProductId);
            
            decimal price;
            if (disc != null && c.Quantity >= disc.MinQty)
            {
                price = disc.DiscountType == DiscountType.Percentage 
                    ? Math.Round(basePrice - (basePrice * disc.DiscountValue / 100), 2)
                    : Math.Round(basePrice - disc.DiscountValue, 2);
            }
            else
            {
                price = c.Product?.DiscountPrice ?? basePrice;
            }

            if (c.ProductVariant?.PriceAdjustment.HasValue == true)
                price += c.ProductVariant.PriceAdjustment!.Value;

            return new CartItemDto(
                c.Id, c.ProductId, c.ProductVariantId,
                c.Product?.NameAr ?? "", c.Product?.NameEn ?? "",
                c.Product?.Images.FirstOrDefault(i => i.IsMain)?.ImageUrl,
                c.ProductVariant?.Size, c.ProductVariant?.Color, c.ProductVariant?.ColorAr,
                c.Quantity, price, price * c.Quantity
            );
        }).ToList();

        var subTotal = dtos.Sum(d => d.TotalPrice);

        // Apply free-delivery threshold from store settings
        var freeAt = store?.FreeDeliveryAt ?? 2000m;
        var appliedFee = subTotal >= freeAt ? 0m : deliveryFee;

        return new CartSummaryDto(dtos, subTotal, appliedFee, subTotal + appliedFee, dtos.Sum(d => d.Quantity));
    }
}
