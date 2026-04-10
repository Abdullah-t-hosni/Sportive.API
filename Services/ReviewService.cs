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
    Task<bool> UpdateProductRatingAsync(int productId);
    Task<bool> ReplyToReviewAsync(int reviewId, string reply, string adminName);
    Task<List<ReviewDto>> GetAllApprovedReviewsAsync();
}

public record ReviewDto(
    int Id, 
    string CustomerName, 
    int Rating, 
    string? Comment, 
    DateTime CreatedAt, 
    bool IsApproved,
    string? ProductName = null,
    string? AdminReply = null,
    DateTime? RepliedAt = null,
    string? RepliedBy = null
);

public class ReviewService : IReviewService
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;

    public ReviewService(AppDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task<List<ReviewDto>> GetApprovedReviewsAsync(int productId)
    {
        return await _db.Reviews
            .Include(r => r.Customer)
            .Where(r => r.ProductId == productId && r.IsApproved)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReviewDto(
                r.Id, 
                r.Customer.FullName, 
                r.Rating, 
                r.Comment, 
                r.CreatedAt, 
                r.IsApproved, 
                null, 
                r.AdminReply, 
                r.RepliedAt, 
                r.RepliedBy
            ))
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

        // 🔔 ALERT ADMIN
        var p = await _db.Products.FindAsync(productId);
        var c = await _db.Customers.FindAsync(customerId);
        await _notifications.SendAsync(
            null, // Admin Group
            "تقييم جديد بانتظار المراجعة", 
            "New Review Pending",
            $"العميل {c?.FullName} قام بتقييم {p?.NameAr}",
            $"Customer {c?.FullName} reviewed {p?.NameEn}",
            "Review"
        );

        return review;
    }

    public async Task<bool> ApproveReviewAsync(int reviewId)
    {
        var r = await _db.Reviews.FindAsync(reviewId);
        if (r == null) return false;
        r.IsApproved = true;
        
        await _db.SaveChangesAsync();
        await UpdateProductRatingAsync(r.ProductId);
        return true;
    }

    public async Task<List<ReviewDto>> GetPendingReviewsAsync()
    {
        return await _db.Reviews
            .Include(r => r.Customer)
            .Include(r => r.Product)
            .Where(r => !r.IsApproved)
            .Select(r => new ReviewDto(
                r.Id, 
                r.Customer.FullName, 
                r.Rating, 
                r.Comment, 
                r.CreatedAt, 
                r.IsApproved, 
                r.Product.NameAr,
                r.AdminReply,
                r.RepliedAt,
                r.RepliedBy
            ))
            .ToListAsync();
    }

    public async Task<bool> DeleteReviewAsync(int reviewId)
    {
        var r = await _db.Reviews.FindAsync(reviewId);
        if (r == null) return false;
        var productId = r.ProductId;
        _db.Reviews.Remove(r);
        var saved = await _db.SaveChangesAsync() > 0;
        if (saved) await UpdateProductRatingAsync(productId);
        return saved;
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

    public async Task<bool> ReplyToReviewAsync(int reviewId, string reply, string adminName)
    {
        var r = await _db.Reviews.FindAsync(reviewId);
        if (r == null) return false;

        r.AdminReply = reply;
        r.RepliedAt = Utils.TimeHelper.GetEgyptTime();
        r.RepliedBy = adminName;
        
        var saved = await _db.SaveChangesAsync() > 0;
        if (saved)
        {
            // 🔔 NOTIFY CUSTOMER
            var customer = await _db.Customers.Include(c => c.AppUser).FirstOrDefaultAsync(c => c.Id == r.CustomerId);
            if (customer?.AppUser != null)
            {
                await _notifications.SendAsync(
                    customer.AppUser.Id,
                    "تم الرد على تقييمك",
                    "Reply to your review",
                    $"قامت الإدارة بالرد على تقييمك لمنتج {r.Product?.NameAr}",
                    $"Admin replied to your review for {r.Product?.NameEn}",
                    "ReviewResponse"
                );
            }
        }

        return saved;
    }

    public async Task<List<ReviewDto>> GetAllApprovedReviewsAsync()
    {
        return await _db.Reviews
            .Include(r => r.Customer)
            .Include(r => r.Product)
            .Where(r => r.IsApproved)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReviewDto(
                r.Id, 
                r.Customer.FullName, 
                r.Rating, 
                r.Comment, 
                r.CreatedAt, 
                r.IsApproved, 
                r.Product.NameAr,
                r.AdminReply,
                r.RepliedAt,
                r.RepliedBy
            ))
            .ToListAsync();
    }

    public async Task<bool> UpdateProductRatingAsync(int productId)
    {
        var product = await _db.Products.FindAsync(productId);
        if (product == null) return false;

        var approvedReviews = await _db.Reviews
            .Where(r => r.ProductId == productId && r.IsApproved)
            .ToListAsync();

        if (approvedReviews.Any())
        {
            product.AverageRating = approvedReviews.Average(r => r.Rating);
            product.ReviewCount = approvedReviews.Count;
        }
        else
        {
            product.AverageRating = 0;
            product.ReviewCount = 0;
        }

        return await _db.SaveChangesAsync() > 0;
    }
}
