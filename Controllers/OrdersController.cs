using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orders;
    private readonly AppDbContext _db;

    public OrdersController(IOrderService orders, AppDbContext db)
    {
        _orders = orders;
        _db = db;
    }

    // Admin: get all orders with optional filters
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] OrderStatus? status = null,
        [FromQuery] string? search = null) =>
        Ok(await _orders.GetOrdersAsync(page, pageSize, status, search));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var order = await _orders.GetOrderByIdAsync(id);
        return order == null ? NotFound() : Ok(order);
    }

    // Customer: get my orders (uses JWT token to find customerId)
    [HttpGet("my")]
    public async Task<IActionResult> GetMyOrders(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var customer = await _db.Customers
            .Where(c => c.AppUserId == userId && !c.IsDeleted)
            .FirstOrDefaultAsync();

        // Fallback: find by email if no AppUserId link
        if (customer == null)
        {
            var email = User.FindFirstValue(ClaimTypes.Email)!;
            customer = await _db.Customers
                .Where(c => c.Email == email && !c.IsDeleted)
                .FirstOrDefaultAsync();

            // Link them for future
            if (customer != null)
            {
                customer.AppUserId = userId;
                await _db.SaveChangesAsync();
            }
        }

        if (customer == null)
            return NotFound(new { message = "Customer profile not found" });

        var result = await _orders.GetOrdersAsync(page, pageSize, null, null, customer.Id);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderDto dto, [FromQuery] int? customerId = null)
    {
        try
        {
            // If customerId not provided, get it from JWT token
            if (!customerId.HasValue)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var customer = await _db.Customers
                    .Where(c => c.AppUserId == userId && !c.IsDeleted)
                    .FirstOrDefaultAsync();
                if (customer == null)
                    return BadRequest(new { message = "Customer profile not found" });
                customerId = customer.Id;
            }

            var order = await _orders.CreateOrderAsync(customerId.Value, dto);
            return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [Authorize(Roles = "Admin")]
    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateOrderStatusDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        try { return Ok(await _orders.UpdateOrderStatusAsync(id, dto, userId)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CartController : ControllerBase
{
    private readonly ICartService _cart;
    private readonly AppDbContext _db;

    public CartController(ICartService cart, AppDbContext db)
    {
        _cart = cart;
        _db = db;
    }

    /// <summary>جيب الـ customerId من التوكن</summary>
    private async Task<int?> GetCustomerIdAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return null;
        var customer = await _db.Customers
            .Where(c => c.AppUserId == userId && !c.IsDeleted)
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync();
        return customer?.Id;
    }

    /// <summary>GET /api/cart — سلة المستخدم الحالي</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return NotFound(new { message = "Customer profile not found" });
        return Ok(await _cart.GetCartAsync(customerId.Value));
    }

    /// <summary>GET /api/cart/{customerId} — للتوافق مع الفرونت القديم</summary>
    [HttpGet("{customerId:int}")]
    public async Task<IActionResult> GetById(int customerId) =>
        Ok(await _cart.GetCartAsync(customerId));

    /// <summary>POST /api/cart — إضافة للسلة (customerId من التوكن)</summary>
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddToCartDto dto)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return NotFound(new { message = "Customer profile not found" });
        return Ok(await _cart.AddToCartAsync(customerId.Value, dto));
    }

    /// <summary>POST /api/cart/{customerId} — للتوافق مع الفرونت القديم</summary>
    [HttpPost("{customerId:int}")]
    public async Task<IActionResult> AddById(int customerId, [FromBody] AddToCartDto dto) =>
        Ok(await _cart.AddToCartAsync(customerId, dto));

    /// <summary>PUT /api/cart/items/{itemId}</summary>
    [HttpPut("items/{itemId}")]
    public async Task<IActionResult> Update(int itemId, [FromBody] UpdateCartItemDto dto)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return NotFound(new { message = "Customer profile not found" });
        try { return Ok(await _cart.UpdateCartItemAsync(customerId.Value, itemId, dto)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>PUT /api/cart/{customerId}/items/{itemId} — للتوافق</summary>
    [HttpPut("{customerId:int}/items/{itemId}")]
    public async Task<IActionResult> UpdateById(int customerId, int itemId, [FromBody] UpdateCartItemDto dto)
    {
        try { return Ok(await _cart.UpdateCartItemAsync(customerId, itemId, dto)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>DELETE /api/cart/items/{itemId}</summary>
    [HttpDelete("items/{itemId}")]
    public async Task<IActionResult> Remove(int itemId)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return NotFound(new { message = "Customer profile not found" });
        try { return Ok(await _cart.RemoveFromCartAsync(customerId.Value, itemId)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>DELETE /api/cart/{customerId}/items/{itemId} — للتوافق</summary>
    [HttpDelete("{customerId:int}/items/{itemId}")]
    public async Task<IActionResult> RemoveById(int customerId, int itemId)
    {
        try { return Ok(await _cart.RemoveFromCartAsync(customerId, itemId)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>DELETE /api/cart — مسح السلة</summary>
    [HttpDelete]
    public async Task<IActionResult> Clear()
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return NotFound(new { message = "Customer profile not found" });
        await _cart.ClearCartAsync(customerId.Value);
        return NoContent();
    }

    /// <summary>DELETE /api/cart/{customerId} — للتوافق</summary>
    [HttpDelete("{customerId:int}")]
    public async Task<IActionResult> ClearById(int customerId)
    {
        await _cart.ClearCartAsync(customerId);
        return NoContent();
    }
}
