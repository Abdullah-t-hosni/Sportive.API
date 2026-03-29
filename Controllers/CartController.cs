using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;

    public CartController(ICartService cartService)
    {
        _cartService = cartService;
    }

    private async Task<int?> GetCustomerIdAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return null;
        var c = await _db.Customers
            .Where(c => c.AppUserId == userId && !c.IsDeleted)
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync();
        return c?.Id;
    }

    /// <summary>الحصول على السلة لعميل معين</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return BadRequest(new { message = "Only customers have shopping carts" });

        var cart = await _cartService.GetCartAsync(customerId.Value);
        return Ok(cart);
    }

    /// <summary>إضافة منتج للسلة</summary>
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddToCartDto dto)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return BadRequest(new { message = "Only customers can add items to cart" });

        var cart = await _cartService.AddToCartAsync(customerId.Value, dto);
        return Ok(cart);
    }

    /// <summary>تحديث كمية منتج في السلة</summary>
    [HttpPut("items/{cartItemId}")]
    public async Task<IActionResult> Update(int cartItemId, [FromBody] UpdateCartItemDto dto)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return Forbid();

        try
        {
            var cart = await _cartService.UpdateCartItemAsync(customerId.Value, cartItemId, dto);
            return Ok(cart);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Item not found in your cart" });
        }
    }

    /// <summary>حذف منتج من السلة</summary>
    [HttpDelete("items/{cartItemId}")]
    public async Task<IActionResult> Remove(int cartItemId)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return Forbid();

        try
        {
            var cart = await _cartService.RemoveFromCartAsync(customerId.Value, cartItemId);
            return Ok(cart);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Item not found in your cart" });
        }
    }

    /// <summary>تفريغ السلة بالكامل</summary>
    [HttpDelete]
    public async Task<IActionResult> Clear()
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return Forbid();

        await _cartService.ClearCartAsync(customerId.Value);
        return NoContent();
    }
}
