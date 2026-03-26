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

    /// <summary>الحصول على السلة لعميل معين</summary>
    [HttpGet("{customerId}")]
    public async Task<IActionResult> Get(int customerId)
    {
        var cart = await _cartService.GetCartAsync(customerId);
        return Ok(cart);
    }

    /// <summary>إضافة منتج للسلة</summary>
    [HttpPost("{customerId}")]
    public async Task<IActionResult> Add(int customerId, [FromBody] AddToCartDto dto)
    {
        var cart = await _cartService.AddToCartAsync(customerId, dto);
        return Ok(cart);
    }

    /// <summary>تحديث كمية منتج في السلة</summary>
    [HttpPut("{customerId}/items/{cartItemId}")]
    public async Task<IActionResult> Update(int customerId, int cartItemId, [FromBody] UpdateCartItemDto dto)
    {
        try
        {
            var cart = await _cartService.UpdateCartItemAsync(customerId, cartItemId, dto);
            return Ok(cart);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Item not found in cart" });
        }
    }

    /// <summary>حذف منتج من السلة</summary>
    [HttpDelete("{customerId}/items/{cartItemId}")]
    public async Task<IActionResult> Remove(int customerId, int cartItemId)
    {
        try
        {
            var cart = await _cartService.RemoveFromCartAsync(customerId, cartItemId);
            return Ok(cart);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Item not found in cart" });
        }
    }

    /// <summary>تفريغ السلة بالكامل</summary>
    [HttpDelete("{customerId}")]
    public async Task<IActionResult> Clear(int customerId)
    {
        await _cartService.ClearCartAsync(customerId);
        return NoContent();
    }
}
