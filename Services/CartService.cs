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
        return BuildSummary(items);
    }

    public async Task<CartSummaryDto> AddToCartAsync(int customerId, AddToCartDto dto)
    {
        if (!await _db.Customers.AnyAsync(c => c.Id == customerId))
            throw new InvalidOperationException(
                "No customer with this id. Create a customer profile or use the correct customer id.");

        if (!await _db.Products.AnyAsync(p => p.Id == dto.ProductId))
            throw new InvalidOperationException("Product not found.");

        // Clients often send 0 instead of omitting the variant — invalid FK
        int? variantId = dto.ProductVariantId is 0 ? null : dto.ProductVariantId;

        if (variantId.HasValue)
        {
            var ok = await _db.ProductVariants.AnyAsync(v =>
                v.Id == variantId.Value && v.ProductId == dto.ProductId && !v.IsDeleted);
            if (!ok)
                throw new InvalidOperationException("Invalid or inactive variant for this product.");
        }

        var existing = await _db.CartItems.FirstOrDefaultAsync(c =>
            c.CustomerId == customerId &&
            c.ProductId == dto.ProductId &&
            c.ProductVariantId == variantId);

        if (existing != null)
            existing.Quantity += dto.Quantity;
        else
            _db.CartItems.Add(new CartItem
            {
                CustomerId       = customerId,
                ProductId        = dto.ProductId,
                ProductVariantId = variantId,
                Quantity         = dto.Quantity
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
            .Include(c => c.Product).ThenInclude(p => p.Images)
            .Include(c => c.ProductVariant)
            .Where(c => c.CustomerId == customerId)
            .ToListAsync();

    private static CartSummaryDto BuildSummary(List<CartItem> items)
    {
        const decimal deliveryFee = 50m;
        const string defaultImageUrl = "https://via.placeholder.com/400x400?text=No+Image";

        var dtos = items.Select(c =>
        {
            var price = c.Product?.DiscountPrice ?? c.Product?.Price ?? 0m;
            if (c.ProductVariant?.PriceAdjustment.HasValue == true)
                price += c.ProductVariant.PriceAdjustment!.Value;

            var imageUrl = c.Product?.Images?.FirstOrDefault(i => i.IsMain)?.ImageUrl ?? defaultImageUrl;

            return new CartItemDto(
                c.Id,
                c.ProductId,
                c.Product?.NameAr ?? string.Empty,
                c.Product?.NameEn ?? string.Empty,
                imageUrl,
                c.ProductVariant?.Size,
                c.ProductVariant?.Color,
                c.ProductVariant?.ColorAr,
                c.Quantity,
                price,
                price * c.Quantity
            );
        }).ToList();

        var subTotal = dtos.Sum(d => d.TotalPrice);
        return new CartSummaryDto(dtos, subTotal, deliveryFee, subTotal + deliveryFee, dtos.Sum(d => d.Quantity));
    }
}
