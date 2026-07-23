using System.Security.Claims;
using Sportive.API.Models;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Services;
using Microsoft.AspNetCore.DataProtection;

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

    [RequirePermission(ModuleKeys.Reviews, requireEdit: true)]
    [HttpPost("admin-add")]
    public async Task<IActionResult> AdminAddReview([FromBody] AdminAddReviewDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.CustomerName))
            return BadRequest(new { message = "Customer name is required." });
        if (dto.ProductId <= 0)
            return BadRequest(new { message = "Product is required." });
        if (dto.Rating < 1 || dto.Rating > 5)
            return BadRequest(new { message = "Rating must be between 1 and 5." });

        try
        {
            var adminName = User.Identity?.Name ?? "Admin";
            var review = await _reviews.AddAdminReviewAsync(dto.CustomerName, dto.CustomerPhone, dto.ProductId, dto.Rating, dto.Comment, adminName);
            return Ok(new { message = "Review added successfully", id = review.Id });
        }
        catch (System.Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("generate-token")]
    public IActionResult GenerateToken([FromBody] GenerateReviewTokenDto dto, [FromServices] IDataProtectionProvider dpProvider)
    {
        var protector = dpProvider.CreateProtector("ReviewTokens");
        var token = protector.Protect($"{dto.OrderId}:{dto.ProductId}");
        return Ok(new { token });
    }

    [HttpGet("validate-token/{token}")]
    public IActionResult ValidateToken(string token, [FromServices] IDataProtectionProvider dpProvider)
    {
        try
        {
            var protector = dpProvider.CreateProtector("ReviewTokens");
            var decrypted = protector.Unprotect(token);
            var parts = decrypted.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var orderId) && int.TryParse(parts[1], out var productId))
            {
                return Ok(new { valid = true, productId = productId, productName = "Product" });
            }
        }
        catch {}
        return BadRequest(new { valid = false });
    }

    [HttpPost("by-token")]
    public async Task<IActionResult> AddByToken([FromBody] AddReviewByTokenDto dto, [FromServices] IDataProtectionProvider dpProvider, [FromServices] Sportive.API.Data.AppDbContext db)
    {
        try
        {
            var protector = dpProvider.CreateProtector("ReviewTokens");
            var decrypted = protector.Unprotect(dto.Token);
            var parts = decrypted.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var orderId) && int.TryParse(parts[1], out var productId))
            {
                var order = await db.Orders.FindAsync(orderId);
                if (order == null) return NotFound(new { message = "Order not found" });
                
                if (order.CustomerId > 0)
                {
                    await _reviews.AddReviewAsync(order.CustomerId, productId, dto.Rating, dto.Comment);
                }
                else
                {
                    await _reviews.AddAdminReviewAsync("Online Guest", null, productId, dto.Rating, dto.Comment, "System (Token)");
                }
                return Ok(new { message = "Review submitted for moderation" });
            }
        }
        catch {}
        return BadRequest(new { message = "Invalid token" });
    }
}

public record ReplyDto(string Reply);
public record AdminAddReviewDto(string CustomerName, string? CustomerPhone, int ProductId, int Rating, string? Comment);
public record GenerateReviewTokenDto(int OrderId, int ProductId);
public record AddReviewByTokenDto(string Token, int Rating, string? Comment);

