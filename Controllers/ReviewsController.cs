using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReviewsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ReviewsController(AppDbContext db) => _db = db;

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

    /// <summary>GET /api/reviews/product/{productId} — تقييمات منتج</summary>
    [HttpGet("product/{productId}")]
    public async Task<IActionResult> GetByProduct(int productId)
    {
        var reviews = await _db.Reviews
            .Include(r => r.Customer)
            .Where(r => r.ProductId == productId && r.IsApproved && !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new {
                r.Id,
                r.Rating,
                r.Comment,
                r.CreatedAt,
                CustomerName = r.Customer.FirstName + " " + r.Customer.LastName
            })
            .ToListAsync();

        return Ok(reviews);
    }

    /// <summary>POST /api/reviews — إضافة تقييم</summary>
    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] CreateReviewDto dto)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null)
            return NotFound(new { message = "Customer profile not found" });

        // تحقق إن المنتج موجود
        var product = await _db.Products.FindAsync(dto.ProductId);
        if (product == null)
            return NotFound(new { message = "Product not found" });

        // تحقق إن العميل ما قيّمش المنتج قبل كده
        var existing = await _db.Reviews
            .AnyAsync(r => r.ProductId == dto.ProductId
                        && r.CustomerId == customerId
                        && !r.IsDeleted);
        if (existing)
            return BadRequest(new { message = "لقد قمت بتقييم هذا المنتج من قبل" });

        var review = new Review
        {
            ProductId  = dto.ProductId,
            CustomerId = customerId.Value,
            Rating     = Math.Clamp(dto.Rating, 1, 5),
            Comment    = dto.Comment,
            IsApproved = false // يحتاج موافقة Admin
        };

        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();

        return Ok(new { message = "تم إرسال تقييمك، سيظهر بعد المراجعة", id = review.Id });
    }

    /// <summary>GET /api/reviews/pending — تقييمات تنتظر الموافقة (Admin)</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        var reviews = await _db.Reviews
            .Include(r => r.Customer)
            .Include(r => r.Product)
            .Where(r => !r.IsApproved && !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new {
                r.Id,
                r.Rating,
                r.Comment,
                r.CreatedAt,
                CustomerName = r.Customer.FirstName + " " + r.Customer.LastName,
                ProductName  = r.Product.NameAr
            })
            .ToListAsync();

        return Ok(reviews);
    }

    /// <summary>PATCH /api/reviews/{id}/approve — موافقة على تقييم (Admin)</summary>
    [Authorize(Roles = "Admin")]
    [HttpPatch("{id}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        var review = await _db.Reviews.FindAsync(id);
        if (review == null) return NotFound();

        review.IsApproved = true;
        review.UpdatedAt  = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { message = "تمت الموافقة على التقييم" });
    }

    /// <summary>DELETE /api/reviews/{id} — حذف تقييم (Admin)</summary>
    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var review = await _db.Reviews.FindAsync(id);
        if (review == null) return NotFound();

        review.IsDeleted = true;
        review.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return NoContent();
    }
}

public record CreateReviewDto(int ProductId, int Rating, string? Comment);
