using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Services;
using System.Security.Claims;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviews;
    private readonly ICustomerService _customers;

    public ReviewsController(IReviewService reviews, ICustomerService customers)
    {
        _reviews = reviews;
        _customers = customers;
    }

    [HttpGet("product/{productId}")]
    public async Task<IActionResult> GetProductReviews(int productId) =>
        Ok(await _reviews.GetApprovedReviewsAsync(productId));

    [Authorize]
    [HttpGet("can-review/{productId}")]
    public async Task<IActionResult> CanReview(int productId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var customerId = await _customers.GetOrCreateCustomerIdByUserIdAsync(userId!);
        if (customerId == 0) return Ok(false);

        return Ok(await _reviews.CanCustomerReviewAsync(customerId, productId));
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> AddReview([FromBody] AddReviewDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var customerId = await _customers.GetOrCreateCustomerIdByUserIdAsync(userId!);
        if (customerId == 0) return Unauthorized();

        try
        {
            var review = await _reviews.AddReviewAsync(customerId, dto.ProductId, dto.Rating, dto.Comment);
            return Ok(new { message = "Review submitted for moderation", id = review.Id });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,Manager")]
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending() => Ok(await _reviews.GetPendingReviewsAsync());

    [Authorize(Roles = "Admin,Manager")]
    [HttpGet("approved")]
    public async Task<IActionResult> GetApproved() => Ok(await _reviews.GetAllApprovedReviewsAsync());

    [Authorize(Roles = "Admin,Manager")]
    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(int id) =>
        await _reviews.ApproveReviewAsync(id) ? Ok() : NotFound();

    [Authorize(Roles = "Admin,Manager")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id) =>
        await _reviews.DeleteReviewAsync(id) ? NoContent() : NotFound();

    [Authorize(Roles = "Admin,Manager")]
    [HttpPost("{id}/reply")]
    public async Task<IActionResult> Reply(int id, [FromBody] ReplyDto dto)
    {
        var adminName = User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
        return await _reviews.ReplyToReviewAsync(id, dto.Reply, adminName) ? Ok() : NotFound();
    }
}

public record ReplyDto(string Reply);
