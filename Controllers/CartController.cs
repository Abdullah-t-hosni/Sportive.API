using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using System.Security.Claims;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;
    private readonly ICustomerService _customerService;
    private readonly AppDbContext _db;

    public CartController(ICartService cartService, ICustomerService customerService, AppDbContext db)
    {
        _cartService = cartService;
        _customerService = customerService;
        _db = db;
    }

    private async Task<int> GetCustomerIdAsync()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return 0;

        return await _customerService.GetOrCreateCustomerIdByUserIdAsync(userId);
    }

    [HttpGet]
    public async Task<ActionResult<CartSummaryDto>> GetCart()
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == 0) return Forbid();

        var cart = await _cartService.GetCartAsync(customerId);
        return Ok(cart);
    }

    [HttpPost("items")]
    public async Task<ActionResult<CartSummaryDto>> AddItem([FromBody] AddToCartDto dto)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == 0) return Forbid();

        var cart = await _cartService.AddToCartAsync(customerId, dto);
        return Ok(cart);
    }

    [HttpPut("items/{itemId}")]
    public async Task<ActionResult<CartSummaryDto>> UpdateItem(int itemId, [FromBody] UpdateCartItemDto dto)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == 0) return Forbid();

        var cart = await _cartService.UpdateCartItemAsync(customerId, itemId, dto);
        return Ok(cart);
    }

    [HttpDelete("items/{itemId}")]
    public async Task<ActionResult<CartSummaryDto>> RemoveItem(int itemId)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == 0) return Forbid();

        var cart = await _cartService.RemoveFromCartAsync(customerId, itemId);
        return Ok(cart);
    }

    [HttpDelete]
    public async Task<IActionResult> ClearCart()
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == 0) return Forbid();

        await _cartService.ClearCartAsync(customerId);
        return NoContent();
    }
}
