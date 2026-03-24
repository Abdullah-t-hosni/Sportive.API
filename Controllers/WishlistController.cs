using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WishlistController : ControllerBase
{
    private readonly AppDbContext _db;

    public WishlistController(AppDbContext db) => _db = db;

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

    /// <summary>GET /api/wishlist — كل المنتجات المحفوظة</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return NotFound(new { message = "Customer not found" });

        var items = await _db.Set<WishlistItem>()
            .Include(w => w.Product!).ThenInclude(p => p!.Images)
            .Include(w => w.Product!).ThenInclude(p => p!.Category)
            .Where(w => w.CustomerId == customerId && !w.IsDeleted)
            .Select(w => new {
                w.Id,
                w.ProductId,
                w.Product!.NameAr,
                w.Product.NameEn,
                w.Product.Price,
                w.Product.DiscountPrice,
                MainImage = w.Product.Images.Where(i => i.IsMain).Select(i => i.ImageUrl).FirstOrDefault(),
                w.CreatedAt
            })
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>POST /api/wishlist — إضافة منتج</summary>
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddWishlistDto dto)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return NotFound(new { message = "Customer not found" });

        var exists = await _db.Set<WishlistItem>()
            .AnyAsync(w => w.CustomerId == customerId && w.ProductId == dto.ProductId && !w.IsDeleted);

        if (!exists)
        {
            _db.Set<WishlistItem>().Add(new WishlistItem
            {
                CustomerId = customerId.Value,
                ProductId  = dto.ProductId
            });
            await _db.SaveChangesAsync();
        }

        return Ok(new { isInWishlist = true });
    }

    /// <summary>DELETE /api/wishlist/{productId} — حذف منتج</summary>
    [HttpDelete("{productId}")]
    public async Task<IActionResult> Remove(int productId)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return NotFound(new { message = "Customer not found" });

        var item = await _db.Set<WishlistItem>()
            .FirstOrDefaultAsync(w => w.CustomerId == customerId && w.ProductId == productId && !w.IsDeleted);

        if (item != null)
        {
            item.IsDeleted = true;
            item.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return Ok(new { isInWishlist = false });
    }

    /// <summary>GET /api/wishlist/check/{productId} — هل المنتج محفوظ؟</summary>
    [HttpGet("check/{productId}")]
    public async Task<IActionResult> Check(int productId)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return Ok(new { isInWishlist = false });

        var exists = await _db.Set<WishlistItem>()
            .AnyAsync(w => w.CustomerId == customerId && w.ProductId == productId && !w.IsDeleted);

        return Ok(new { isInWishlist = exists });
    }

    /// <summary>GET /api/wishlist/check-bulk — فحص عدة منتجات دفعة واحدة</summary>
    [HttpPost("check-bulk")]
    public async Task<IActionResult> CheckBulk([FromBody] List<int> productIds)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null)
            return Ok(productIds.ToDictionary(id => id, _ => false));

        var wishlistIds = await _db.Set<WishlistItem>()
            .Where(w => w.CustomerId == customerId
                     && productIds.Contains(w.ProductId)
                     && !w.IsDeleted)
            .Select(w => w.ProductId)
            .ToListAsync();

        var result = productIds.ToDictionary(id => id, id => wishlistIds.Contains(id));
        return Ok(result);
    }
}

public record AddWishlistDto(int ProductId);
