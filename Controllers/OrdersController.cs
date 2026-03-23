using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
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
    private readonly ICustomerService _customers;
    private readonly UserManager<AppUser> _users;

    public OrdersController(
        IOrderService orders,
        ICustomerService customers,
        UserManager<AppUser> users)
    {
        _orders    = orders;
        _customers = customers;
        _users     = users;
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

    // Customer: get my orders (linked via Customer.AppUserId or same email as account)
    [HttpGet("my")]
    public async Task<IActionResult> GetMyOrders(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var appUser = await _users.GetUserAsync(User);
        if (appUser == null) return Unauthorized();

        var customerId = await _customers.EnsureCustomerProfileAsync(appUser);
        return Ok(await _orders.GetCustomerOrdersAsync(customerId, page, pageSize));
    }

    /// <summary>
    /// Customers: omit <c>customerId</c> — the server uses the logged-in profile (same id as cart from login).
    /// Admins: pass <c>?customerId=</c> to place an order for that customer.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderDto dto, [FromQuery] int? customerId = null)
    {
        int resolvedCustomerId;

        if (User.IsInRole("Admin"))
        {
            if (!customerId.HasValue)
                return BadRequest(new { message = "Admin must pass ?customerId=<id> for the customer who is checking out." });
            resolvedCustomerId = customerId.Value;
        }
        else if (User.IsInRole("Customer"))
        {
            var appUser = await _users.GetUserAsync(User);
            if (appUser == null)
                return Unauthorized();
            resolvedCustomerId = await _customers.EnsureCustomerProfileAsync(appUser);
        }
        else
        {
            return BadRequest(new { message = "Only Customer or Admin accounts can create orders." });
        }

        try
        {
            var order = await _orders.CreateOrderAsync(resolvedCustomerId, dto);
            return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
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
    private readonly ICustomerService _customers;
    private readonly UserManager<AppUser> _users;

    public CartController(ICartService cart, ICustomerService customers, UserManager<AppUser> users)
    {
        _cart = cart;
        _customers = customers;
        _users = users;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var appUser = await _users.GetUserAsync(User);
        if (appUser == null) return Unauthorized();
        var customerId = await _customers.EnsureCustomerProfileAsync(appUser);
        return Ok(await _cart.GetCartAsync(customerId));
    }

    [HttpPost("me/items")]
    public async Task<IActionResult> AddMe([FromBody] AddToCartDto dto)
    {
        var appUser = await _users.GetUserAsync(User);
        if (appUser == null) return Unauthorized();
        var customerId = await _customers.EnsureCustomerProfileAsync(appUser);
        return Ok(await _cart.AddToCartAsync(customerId, dto));
    }

    [HttpDelete("me")]
    public async Task<IActionResult> ClearMe()
    {
        var appUser = await _users.GetUserAsync(User);
        if (appUser == null) return Unauthorized();
        var customerId = await _customers.EnsureCustomerProfileAsync(appUser);
        await _cart.ClearCartAsync(customerId);
        return NoContent();
    }

    [HttpGet("{customerId}")]
    public async Task<IActionResult> Get(int customerId) =>
        Ok(await _cart.GetCartAsync(customerId));

    [HttpPost("{customerId}")]
    public async Task<IActionResult> Add(int customerId, [FromBody] AddToCartDto dto) =>
        Ok(await _cart.AddToCartAsync(customerId, dto));

    [HttpPut("{customerId}/items/{itemId}")]
    public async Task<IActionResult> Update(int customerId, int itemId, [FromBody] UpdateCartItemDto dto)
    {
        try { return Ok(await _cart.UpdateCartItemAsync(customerId, itemId, dto)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpDelete("{customerId}/items/{itemId}")]
    public async Task<IActionResult> Remove(int customerId, int itemId)
    {
        try { return Ok(await _cart.RemoveFromCartAsync(customerId, itemId)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpDelete("{customerId}")]
    public async Task<IActionResult> Clear(int customerId)
    {
        await _cart.ClearCartAsync(customerId);
        return NoContent();
    }
}
