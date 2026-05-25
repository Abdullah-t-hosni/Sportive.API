using Sportive.API.DTOs;

namespace Sportive.API.Interfaces;

public interface ICartService
{
    Task<CartSummaryDto> GetCartAsync(int customerId);
    Task<CartSummaryDto> AddToCartAsync(int customerId, AddToCartDto dto);
    Task<CartSummaryDto> BulkAddToCartAsync(int customerId, BulkAddToCartDto dto);
    Task<CartSummaryDto> UpdateCartItemAsync(int customerId, int cartItemId, UpdateCartItemDto dto);
    Task<CartSummaryDto> RemoveFromCartAsync(int customerId, int cartItemId);
    Task ClearCartAsync(int customerId);
}
