using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using System.Security.Claims;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;
    private readonly AppDbContext _db;

    public CartController(ICartService cartService, AppDbContext db)
    {
        _cartService = cartService;
        _db = db;
    }

    private async Task<int?> GetCustomerIdAsync()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null) return null;
        var c = await _db.Customers
            .Where(c => c.AppUserId == userId && !c.IsDeleted)
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync();
        return c?.Id;
    }

    /// <summary>الحصول على السلة لعميل معين</summary>
    [HttpGet]
    [HttpGet("{customerId}")]
    public async Task<IActionResult> Get(int? customerId = null)
    {
        var loggedInId = await GetCustomerIdAsync();
        var targetId = customerId ?? loggedInId;

        if (targetId == null) return BadRequest(new { message = "Only customers have shopping carts" });

        if (customerId.HasValue)
        {
            bool isStaff = User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Staff") || User.IsInRole("Cashier");
            if (!isStaff && (!loggedInId.HasValue || customerId.Value != loggedInId.Value))
            {
                return Forbid();
            }
        }

        var cart = await _cartService.GetCartAsync(targetId.Value);
        return Ok(cart);
    }

    /// <summary>إضافة منتج للسلة</summary>
    [HttpPost]
    [HttpPost("{customerId}")]
    public async Task<IActionResult> Add(int? customerId, [FromBody] AddToCartDto dto)
    {
        var loggedInId = await GetCustomerIdAsync();
        var targetId = customerId ?? loggedInId;

        if (targetId == null) return BadRequest(new { message = "Only customers can add items to cart" });

        if (customerId.HasValue && loggedInId.HasValue && customerId.Value != loggedInId.Value)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager") && !User.IsInRole("Staff") && !User.IsInRole("Cashier"))
                return Forbid();
        }

        var cart = await _cartService.AddToCartAsync(targetId.Value, dto);
        return Ok(cart);
    }

    /// <summary>تحديث كمية منتج في السلة</summary>
    [HttpPut("items/{cartItemId}")]
    [HttpPut("{customerId}/items/{cartItemId}")]
    public async Task<IActionResult> Update(int cartItemId, [FromBody] UpdateCartItemDto dto, int? customerId = null)
    {
        var loggedInId = await GetCustomerIdAsync();
        var targetId = customerId ?? loggedInId;

        if (targetId == null) return Forbid();

        if (customerId.HasValue)
        {
            bool isStaff = User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Staff") || User.IsInRole("Cashier");
            if (!isStaff && (!loggedInId.HasValue || customerId.Value != loggedInId.Value))
            {
                return Forbid();
            }
        }

        try
        {
            var cart = await _cartService.UpdateCartItemAsync(targetId.Value, cartItemId, dto);
            return Ok(cart);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Item not found in your cart" });
        }
    }

    /// <summary>حذف منتج من السلة</summary>
    [HttpDelete("items/{cartItemId}")]
    [HttpDelete("{customerId}/items/{cartItemId}")]
    public async Task<IActionResult> Remove(int cartItemId, int? customerId = null)
    {
        var loggedInId = await GetCustomerIdAsync();
        var targetId = customerId ?? loggedInId;

        if (targetId == null) return Forbid();

        if (customerId.HasValue && loggedInId.HasValue && customerId.Value != loggedInId.Value)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager") && !User.IsInRole("Staff") && !User.IsInRole("Cashier"))
                return Forbid();
        }

        try
        {
            var cart = await _cartService.RemoveFromCartAsync(targetId.Value, cartItemId);
            return Ok(cart);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Item not found in your cart" });
        }
    }

    /// <summary>تفريغ السلة بالكامل</summary>
    [HttpDelete]
    [HttpDelete("{customerId}")]
    public async Task<IActionResult> Clear(int? customerId = null)
    {
        var loggedInId = await GetCustomerIdAsync();
        var targetId = customerId ?? loggedInId;

        if (targetId == null) return Forbid();

        if (customerId.HasValue && loggedInId.HasValue && customerId.Value != loggedInId.Value)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager") && !User.IsInRole("Staff") && !User.IsInRole("Cashier"))
                return Forbid();
        }

        await _cartService.ClearCartAsync(targetId.Value);
        return NoContent();
    }
}
