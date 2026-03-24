using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WishlistController : ControllerBase
{
    private readonly IWishlistService _wishlist;
    private readonly AppDbContext _db;

    public WishlistController(IWishlistService wishlist, AppDbContext db)
    {
        _wishlist = wishlist;
        _db = db;
    }

    private async Task<int?> GetCustomerIdAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return null;
        var customer = await _db.Customers
            .Where(c => c.AppUserId == userId && !c.IsDeleted)
            .FirstOrDefaultAsync();
        return customer?.Id;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var custId = await GetCustomerIdAsync();
        if (custId == null) return NotFound(new { message = "Customer not found" });
        return Ok(await _wishlist.GetWishlistAsync(custId.Value));
    }

    [HttpPost("{productId}")]
    public async Task<IActionResult> Add(int productId)
    {
        var custId = await GetCustomerIdAsync();
        if (custId == null) return NotFound(new { message = "Customer not found" });
        return await _wishlist.AddToWishlistAsync(custId.Value, productId) ? Ok() : BadRequest();
    }

    [HttpDelete("{productId}")]
    public async Task<IActionResult> Remove(int productId)
    {
        var custId = await GetCustomerIdAsync();
        if (custId == null) return NotFound(new { message = "Customer not found" });
        return await _wishlist.RemoveFromWishlistAsync(custId.Value, productId) ? Ok() : NotFound();
    }

    [HttpGet("check/{productId}")]
    public async Task<IActionResult> Check(int productId)
    {
        var custId = await GetCustomerIdAsync();
        if (custId == null) return Ok(false);
        return Ok(await _wishlist.IsInWishlistAsync(custId.Value, productId));
    }
}
