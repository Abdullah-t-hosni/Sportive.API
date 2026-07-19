using System.Security.Claims;
using Sportive.API.Models;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Services;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviews;
    private readonly ICustomerService _customers;
    private readonly AppDbContext _db;

    public ReviewsController(IReviewService reviews, ICustomerService customers, AppDbContext db)
    {
        _reviews = reviews;
        _customers = customers;
        _db = db;
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

    // ─── Guest Review Token Endpoints ────────────────────────────────────────

    /// <summary>
    /// Admin calls this to generate a one-time review link for a guest customer.
    /// </summary>
    [RequirePermission(ModuleKeys.Reviews, requireEdit: true)]
    [HttpPost("generate-token")]
    public async Task<IActionResult> GenerateReviewToken([FromBody] GenerateReviewTokenDto dto)
    {
        // Verify the order exists and belongs to the customer
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == dto.OrderId && o.Status == OrderStatus.Delivered);
        if (order == null)
            return BadRequest(new { message = "الطلب غير موجود أو لم يتم توصيله بعد." });

        var hasProduct = order.Items.Any(i => i.ProductId == dto.ProductId);
        if (!hasProduct)
            return BadRequest(new { message = "المنتج غير موجود في هذا الطلب." });

        var token = await _reviews.GenerateReviewTokenAsync(dto.OrderId, dto.ProductId, order.CustomerId);
        return Ok(new { token });
    }

    /// <summary>
    /// Guest customer submits a review using a one-time token (no login required).
    /// </summary>
    [HttpPost("by-token")]
    public async Task<IActionResult> AddReviewByToken([FromBody] AddReviewByTokenDto dto)
    {
        try
        {
            var review = await _reviews.AddReviewByTokenAsync(dto.Token, dto.Rating, dto.Comment);
            return Ok(new { message = "شكراً! تم إرسال تقييمك وسيظهر بعد المراجعة." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Validates a token and returns product info (for the guest review page).
    /// </summary>
    [HttpGet("validate-token/{token}")]
    public async Task<IActionResult> ValidateToken(string token)
    {
        var (valid, customerId, productId) = await _reviews.ValidateReviewTokenAsync(token);
        if (!valid) return Ok(new { valid = false });

        var product = await _db.Products.FindAsync(productId);
        return Ok(new { valid = true, productId, productName = product?.NameAr ?? product?.NameEn });
    }

    // ─── Admin Endpoints ──────────────────────────────────────────────────────

    [RequirePermission(ModuleKeys.Reviews, requireEdit: true)]
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending() => Ok(await _reviews.GetPendingReviewsAsync());

    [RequirePermission(ModuleKeys.Reviews, requireEdit: true)]
    [HttpGet("approved")]
    public async Task<IActionResult> GetApproved() => Ok(await _reviews.GetAllApprovedReviewsAsync());

    [RequirePermission(ModuleKeys.Reviews, requireEdit: true)]
    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(int id) =>
        await _reviews.ApproveReviewAsync(id) ? Ok() : NotFound();

    [RequirePermission(ModuleKeys.Reviews, requireEdit: true)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id) =>
        await _reviews.DeleteReviewAsync(id) ? NoContent() : NotFound();

    [RequirePermission(ModuleKeys.Reviews, requireEdit: true)]
    [HttpPost("{id}/reply")]
    public async Task<IActionResult> Reply(int id, [FromBody] ReplyDto dto)
    {
        var adminName = User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
        return await _reviews.ReplyToReviewAsync(id, dto.Reply, adminName) ? Ok() : NotFound();
    }
}

public record ReplyDto(string Reply);
public record GenerateReviewTokenDto(int OrderId, int ProductId);
public record AddReviewByTokenDto(string Token, int Rating, string? Comment);

