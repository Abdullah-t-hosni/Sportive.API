using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviews;
    private readonly AppDbContext _db;

    public ReviewsController(IReviewService reviews, AppDbContext db)
    {
        _reviews = reviews;
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

    [HttpGet("product/{productId}")]
    public async Task<IActionResult> GetProductReviews(int productId) =>
        Ok(await _reviews.GetApprovedReviewsAsync(productId));

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> AddReview([FromBody] CreateReviewDto dto)
    {
        var custId = await GetCustomerIdAsync();
        if (custId == null) return NotFound(new { message = "Customer not found" });

        var review = await _reviews.AddReviewAsync(custId.Value, dto.ProductId, dto.Rating, dto.Comment);
        return Ok(review);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending() =>
        Ok(await _reviews.GetPendingReviewsAsync());

    [Authorize(Roles = "Admin")]
    [HttpPatch("{id}/approve")]
    public async Task<IActionResult> Approve(int id) =>
        await _reviews.ApproveReviewAsync(id) ? Ok() : NotFound();

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id) =>
        await _reviews.DeleteReviewAsync(id) ? NoContent() : NotFound();
}

public record CreateReviewDto(int ProductId, int Rating, string? Comment);
