using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Services;

public interface IReviewService
{
    Task<List<ReviewDto>> GetApprovedReviewsAsync(int productId);
    Task<Review> AddReviewAsync(int customerId, int productId, int rating, string? comment);
    Task<bool> ApproveReviewAsync(int reviewId);
    Task<List<ReviewDto>> GetPendingReviewsAsync();
    Task<bool> DeleteReviewAsync(int reviewId);
    Task<bool> CanCustomerReviewAsync(int customerId, int productId);
}

public record ReviewDto(int Id, string CustomerName, int Rating, string? Comment, DateTime CreatedAt, bool IsApproved);

public class ReviewService : IReviewService
{
    private readonly AppDbContext _db;
    public ReviewService(AppDbContext db) => _db = db;

    public async Task<List<ReviewDto>> GetApprovedReviewsAsync(int productId)
    {
        return await _db.Reviews
            .Include(r => r.Customer)
            .Where(r => r.ProductId == productId && r.IsApproved)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReviewDto(r.Id, r.Customer.FullName, r.Rating, r.Comment, r.CreatedAt, r.IsApproved))
            .ToListAsync();
    }

    public async Task<Review> AddReviewAsync(int customerId, int productId, int rating, string? comment)
    {
        // 🛡️ SECURITY: Double check eligibility even if UI hides the form
        if (!await CanCustomerReviewAsync(customerId, productId))
            throw new InvalidOperationException("You can only review products you have purchased and received.");

        var existing = await _db.Reviews.FirstOrDefaultAsync(r => r.CustomerId == customerId && r.ProductId == productId);
        if (existing != null)
        {
            // Update existing instead of creating new? Or just throw.
            existing.Rating = rating;
            existing.Comment = comment;
            existing.IsApproved = false; // Reset for moderation
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return existing;
        }

        var review = new Review {
            CustomerId = customerId,
            ProductId = productId,
            Rating = rating,
            Comment = comment,
            IsApproved = false // Pending moderate
        };
        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();
        return review;
    }

    public async Task<bool> ApproveReviewAsync(int reviewId)
    {
        var r = await _db.Reviews.FindAsync(reviewId);
        if (r == null) return false;
        r.IsApproved = true;
        
        // Update product's average rating cached data if needed? 
        // Our ProductDetailPage calculates it from reviews so it's fine.
        
        return await _db.SaveChangesAsync() > 0;
    }

    public async Task<List<ReviewDto>> GetPendingReviewsAsync()
    {
        return await _db.Reviews
            .Include(r => r.Customer)
            .Include(r => r.Product)
            .Where(r => !r.IsApproved)
            .Select(r => new ReviewDto(r.Id, $"{r.Customer.FullName} ({r.Product.NameAr})", r.Rating, r.Comment, r.CreatedAt, r.IsApproved))
            .ToListAsync();
    }

    public async Task<bool> DeleteReviewAsync(int reviewId)
    {
        var r = await _db.Reviews.FindAsync(reviewId);
        if (r == null) return false;
        _db.Reviews.Remove(r);
        return await _db.SaveChangesAsync() > 0;
    }

    public async Task<bool> CanCustomerReviewAsync(int customerId, int productId)
    {
        // 💡 VERIFIED PURCHASE CHECK:
        // Product must be in an order with status 'Completed' (Delivered)
        return await _db.OrderItems
            .Include(oi => oi.Order)
            .AnyAsync(oi => oi.ProductId == productId && 
                            oi.Order.CustomerId == customerId && 
                            oi.Order.Status == OrderStatus.Delivered);
    }
}
