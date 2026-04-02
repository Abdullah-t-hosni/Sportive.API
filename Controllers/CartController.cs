using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using System.Security.Claims;

namespace Sportive.API.Controllers;

// ✅ FIX: Swashbuckle 7 crashes when multiple [HttpXxx] attributes are stacked on one method.
// Solution: split each stacked route into two separate action methods.
// The {customerId} routes match what the frontend always sends.
// The bare routes (no customerId) extract it from the JWT for direct customer use.

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

    private bool IsStaff() =>
        User.IsInRole("Admin") || User.IsInRole("Manager") ||
        User.IsInRole("Staff")  || User.IsInRole("Cashier");

    // ── GET CART ─────────────────────────────────────────────────

    /// <summary>سلة عميل معين — المستخدم من الـ Token أو Admin/Staff</summary>
    [HttpGet("{customerId}")]
    public async Task<IActionResult> Get(int customerId)
    {
        var loggedInId = await GetCustomerIdAsync();
        if (!IsStaff() && (loggedInId == null || loggedInId.Value != customerId))
            return Forbid();
        return Ok(await _cartService.GetCartAsync(customerId));
    }

    /// <summary>سلة المستخدم الحالي من الـ Token</summary>
    [HttpGet]
    [ApiExplorerSettings(IgnoreApi = true)] // hidden from Swagger — use GET /cart/{id}
    public async Task<IActionResult> GetOwn()
    {
        var id = await GetCustomerIdAsync();
        if (id == null) return BadRequest(new { message = "Only customers have shopping carts" });
        return Ok(await _cartService.GetCartAsync(id.Value));
    }

    // ── ADD TO CART ──────────────────────────────────────────────

    /// <summary>إضافة منتج لسلة عميل معين</summary>
    [HttpPost("{customerId}")]
    public async Task<IActionResult> Add(int customerId, [FromBody] AddToCartDto dto)
    {
        var loggedInId = await GetCustomerIdAsync();
        if (!IsStaff() && (loggedInId == null || loggedInId.Value != customerId))
            return Forbid();
        return Ok(await _cartService.AddToCartAsync(customerId, dto));
    }

    /// <summary>إضافة منتج لسلة المستخدم الحالي</summary>
    [HttpPost]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> AddOwn([FromBody] AddToCartDto dto)
    {
        var id = await GetCustomerIdAsync();
        if (id == null) return BadRequest(new { message = "Only customers can add items to cart" });
        return Ok(await _cartService.AddToCartAsync(id.Value, dto));
    }

    // ── UPDATE CART ITEM ─────────────────────────────────────────

    /// <summary>تحديث كمية منتج في سلة عميل معين</summary>
    [HttpPut("{customerId}/items/{cartItemId}")]
    public async Task<IActionResult> Update(int customerId, int cartItemId, [FromBody] UpdateCartItemDto dto)
    {
        var loggedInId = await GetCustomerIdAsync();
        if (!IsStaff() && (loggedInId == null || loggedInId.Value != customerId))
            return Forbid();
        try { return Ok(await _cartService.UpdateCartItemAsync(customerId, cartItemId, dto)); }
        catch (KeyNotFoundException) { return NotFound(new { message = "Item not found in your cart" }); }
    }

    /// <summary>تحديث كمية منتج في سلة المستخدم الحالي</summary>
    [HttpPut("items/{cartItemId}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> UpdateOwn(int cartItemId, [FromBody] UpdateCartItemDto dto)
    {
        var id = await GetCustomerIdAsync();
        if (id == null) return Forbid();
        try { return Ok(await _cartService.UpdateCartItemAsync(id.Value, cartItemId, dto)); }
        catch (KeyNotFoundException) { return NotFound(new { message = "Item not found in your cart" }); }
    }

    // ── REMOVE FROM CART ─────────────────────────────────────────

    /// <summary>حذف منتج من سلة عميل معين</summary>
    [HttpDelete("{customerId}/items/{cartItemId}")]
    public async Task<IActionResult> Remove(int customerId, int cartItemId)
    {
        var loggedInId = await GetCustomerIdAsync();
        if (!IsStaff() && (loggedInId == null || loggedInId.Value != customerId))
            return Forbid();
        try { return Ok(await _cartService.RemoveFromCartAsync(customerId, cartItemId)); }
        catch (KeyNotFoundException) { return NotFound(new { message = "Item not found in your cart" }); }
    }

    /// <summary>حذف منتج من سلة المستخدم الحالي</summary>
    [HttpDelete("items/{cartItemId}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> RemoveOwn(int cartItemId)
    {
        var id = await GetCustomerIdAsync();
        if (id == null) return Forbid();
        try { return Ok(await _cartService.RemoveFromCartAsync(id.Value, cartItemId)); }
        catch (KeyNotFoundException) { return NotFound(new { message = "Item not found in your cart" }); }
    }

    // ── CLEAR CART ───────────────────────────────────────────────

    /// <summary>تفريغ سلة عميل معين</summary>
    [HttpDelete("{customerId}")]
    public async Task<IActionResult> Clear(int customerId)
    {
        var loggedInId = await GetCustomerIdAsync();
        if (!IsStaff() && (loggedInId == null || loggedInId.Value != customerId))
            return Forbid();
        await _cartService.ClearCartAsync(customerId);
        return NoContent();
    }

    /// <summary>تفريغ سلة المستخدم الحالي</summary>
    [HttpDelete]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> ClearOwn()
    {
        var id = await GetCustomerIdAsync();
        if (id == null) return Forbid();
        await _cartService.ClearCartAsync(id.Value);
        return NoContent();
    }
}
