using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

// ═══════════════════════════════════════════
// REVIEWS
// ═══════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
public class ReviewsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ReviewsController(AppDbContext db) => _db = db;

    /// <summary>تقييمات منتج معين (public)</summary>
    [HttpGet("product/{productId}")]
    public async Task<IActionResult> GetByProduct(int productId)
    {
        var reviews = await _db.Reviews
            .Include(r => r.Customer)
            .Where(r => r.ProductId == productId && r.IsApproved)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new {
                r.Id,
                r.Rating,
                r.Comment,
                r.CreatedAt,
                CustomerName = r.Customer.FirstName + " " + r.Customer.LastName,
            })
            .ToListAsync();

        return Ok(reviews);
    }

    /// <summary>إضافة تقييم (Customer)</summary>
    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddReviewDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var customer = await _db.Customers
            .Where(c => c.AppUserId == userId && !c.IsDeleted)
            .FirstOrDefaultAsync();

        if (customer == null)
            return BadRequest(new { message = "Customer profile not found" });

        // تحقق إن المنتج موجود
        var product = await _db.Products.FindAsync(dto.ProductId);
        if (product == null) return NotFound(new { message = "المنتج غير موجود" });

        // تحقق إن العميل اشترى المنتج
        var hasPurchased = await _db.OrderItems
            .AnyAsync(i => i.ProductId == dto.ProductId &&
                          _db.Orders.Any(o => o.Id == i.OrderId &&
                                             o.CustomerId == customer.Id &&
                                             o.Status == OrderStatus.Delivered));

        // تحقق إنه ما قيّمش المنتج قبل كده
        var alreadyReviewed = await _db.Reviews
            .AnyAsync(r => r.ProductId == dto.ProductId && r.CustomerId == customer.Id);

        if (alreadyReviewed)
            return BadRequest(new { message = "لقد قيّمت هذا المنتج من قبل" });

        if (dto.Rating < 1 || dto.Rating > 5)
            return BadRequest(new { message = "التقييم لازم يكون بين 1 و 5" });

        var review = new Review
        {
            ProductId  = dto.ProductId,
            CustomerId = customer.Id,
            Rating     = dto.Rating,
            Comment    = dto.Comment,
            IsApproved = true // auto-approve — غيّره لـ false لو عايز manual approval
        };

        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();

        return Ok(new { review.Id, review.Rating, review.Comment, review.CreatedAt });
    }

    /// <summary>التقييمات المعلقة (Admin)</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        var reviews = await _db.Reviews
            .Include(r => r.Customer)
            .Include(r => r.Product)
            .Where(r => !r.IsApproved)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new {
                r.Id, r.Rating, r.Comment, r.CreatedAt,
                CustomerName = r.Customer.FirstName + " " + r.Customer.LastName,
                ProductNameAr = r.Product.NameAr,
                ProductNameEn = r.Product.NameEn,
            })
            .ToListAsync();

        return Ok(reviews);
    }

    /// <summary>الموافقة على تقييم (Admin)</summary>
    [Authorize(Roles = "Admin")]
    [HttpPatch("{id}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        var review = await _db.Reviews.FindAsync(id);
        if (review == null) return NotFound();
        review.IsApproved = true;
        review.UpdatedAt  = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "تم الموافقة على التقييم" });
    }

    /// <summary>حذف تقييم (Admin)</summary>
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

// ═══════════════════════════════════════════
// WISHLIST
// ═══════════════════════════════════════════
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
        var customer = await _db.Customers
            .Where(c => c.AppUserId == userId && !c.IsDeleted)
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync();
        return customer?.Id;
    }

    /// <summary>قائمة المفضلة</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return NotFound(new { message = "Customer profile not found" });

        var items = await _db.WishlistItems
            .Include(w => w.Product).ThenInclude(p => p.Images)
            .Include(w => w.Product).ThenInclude(p => p.Category)
            .Where(w => w.CustomerId == customerId)
            .Select(w => new {
                w.Id,
                w.ProductId,
                w.Product.NameAr,
                w.Product.NameEn,
                w.Product.Price,
                w.Product.DiscountPrice,
                w.Product.Brand,
                MainImage = w.Product.Images
                    .Where(i => i.IsMain).Select(i => i.ImageUrl).FirstOrDefault(),
                w.CreatedAt
            })
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>إضافة للمفضلة</summary>
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddToWishlistDto dto)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return NotFound(new { message = "Customer profile not found" });

        var exists = await _db.WishlistItems
            .AnyAsync(w => w.CustomerId == customerId && w.ProductId == dto.ProductId);

        if (exists) return Ok(new { message = "المنتج موجود بالفعل في المفضلة" });

        var product = await _db.Products.FindAsync(dto.ProductId);
        if (product == null) return NotFound(new { message = "المنتج غير موجود" });

        _db.WishlistItems.Add(new WishlistItem
        {
            CustomerId = customerId.Value,
            ProductId  = dto.ProductId
        });
        await _db.SaveChangesAsync();

        return Ok(new { message = "تمت الإضافة للمفضلة" });
    }

    /// <summary>حذف من المفضلة</summary>
    [HttpDelete("{productId}")]
    public async Task<IActionResult> Remove(int productId)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return NotFound(new { message = "Customer profile not found" });

        var item = await _db.WishlistItems
            .FirstOrDefaultAsync(w => w.CustomerId == customerId && w.ProductId == productId);

        if (item == null) return NotFound();

        _db.WishlistItems.Remove(item);
        await _db.SaveChangesAsync();

        return Ok(new { message = "تم الحذف من المفضلة" });
    }

    /// <summary>تحقق إن المنتج في المفضلة</summary>
    [HttpGet("check/{productId}")]
    public async Task<IActionResult> Check(int productId)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return Ok(new { isInWishlist = false });

        var exists = await _db.WishlistItems
            .AnyAsync(w => w.CustomerId == customerId && w.ProductId == productId);

        return Ok(new { isInWishlist = exists });
    }
}

// ═══════════════════════════════════════════
// NOTIFICATIONS
// ═══════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    public NotificationsController(AppDbContext db) => _db = db;

    private async Task<int?> GetCustomerIdAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return null;
        var customer = await _db.Customers
            .Where(c => c.AppUserId == userId && !c.IsDeleted)
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync();
        return customer?.Id;
    }

    /// <summary>كل الإشعارات</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return NotFound(new { message = "Customer profile not found" });

        var notifications = await _db.Notifications
            .Where(n => n.CustomerId == customerId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new {
                n.Id,
                n.TitleAr, n.TitleEn,
                n.MessageAr, n.MessageEn,
                n.Type, n.IsRead,
                n.ActionUrl, n.CreatedAt
            })
            .ToListAsync();

        return Ok(notifications);
    }

    /// <summary>عدد الإشعارات غير المقروءة</summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return Ok(0);

        var count = await _db.Notifications
            .CountAsync(n => n.CustomerId == customerId && !n.IsRead);

        return Ok(count);
    }

    /// <summary>تعليم إشعار كمقروء</summary>
    [HttpPatch("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return NotFound();

        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.CustomerId == customerId);

        if (notification == null) return NotFound();

        notification.IsRead   = true;
        notification.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { message = "تم التعليم كمقروء" });
    }

    /// <summary>تعليم كل الإشعارات كمقروءة</summary>
    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var customerId = await GetCustomerIdAsync();
        if (customerId == null) return NotFound();

        var unread = await _db.Notifications
            .Where(n => n.CustomerId == customerId && !n.IsRead)
            .ToListAsync();

        foreach (var n in unread) { n.IsRead = true; n.UpdatedAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync();

        return Ok(new { message = $"تم تعليم {unread.Count} إشعار كمقروء" });
    }

    /// <summary>إرسال إشعار (Admin)</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendNotificationDto dto)
    {
        if (dto.CustomerId.HasValue)
        {
            // إشعار لعميل محدد
            _db.Notifications.Add(new Notification
            {
                CustomerId = dto.CustomerId.Value,
                TitleAr = dto.TitleAr, TitleEn = dto.TitleEn,
                MessageAr = dto.MessageAr, MessageEn = dto.MessageEn,
                Type = dto.Type, ActionUrl = dto.ActionUrl
            });
        }
        else
        {
            // إشعار لكل العملاء
            var customerIds = await _db.Customers
                .Where(c => !c.IsDeleted)
                .Select(c => c.Id)
                .ToListAsync();

            foreach (var cId in customerIds)
            {
                _db.Notifications.Add(new Notification
                {
                    CustomerId = cId,
                    TitleAr = dto.TitleAr, TitleEn = dto.TitleEn,
                    MessageAr = dto.MessageAr, MessageEn = dto.MessageEn,
                    Type = dto.Type, ActionUrl = dto.ActionUrl
                });
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "تم إرسال الإشعار" });
    }
}

// ═══════════════════════════════════════════
// ANALYTICS (Admin Dashboard Extended)
// ═══════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AnalyticsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AnalyticsController(AppDbContext db) => _db = db;

    /// <summary>إحصائيات شاملة للأدمن</summary>
    [HttpGet("admin-stats")]
    public async Task<IActionResult> GetAdminStats()
    {
        var now        = DateTime.UtcNow;
        var todayStart = now.Date;
        var todayEnd   = todayStart.AddDays(1);
        var weekStart  = todayStart.AddDays(-7);
        var monthStart = new DateTime(now.Year, now.Month, 1);

        var todayRevenue = await _db.Orders
            .Where(o => o.CreatedAt >= todayStart && o.CreatedAt < todayEnd
                     && o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var weekRevenue = await _db.Orders
            .Where(o => o.CreatedAt >= weekStart && o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var monthRevenue = await _db.Orders
            .Where(o => o.CreatedAt >= monthStart && o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var totalRevenue = await _db.Orders
            .Where(o => o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var newCustomersToday = await _db.Customers
            .CountAsync(c => c.CreatedAt >= todayStart && c.CreatedAt < todayEnd && !c.IsDeleted);

        var newCustomersMonth = await _db.Customers
            .CountAsync(c => c.CreatedAt >= monthStart && !c.IsDeleted);

        var totalCustomers = await _db.Customers.CountAsync(c => !c.IsDeleted);
        var totalProducts  = await _db.Products.CountAsync(p => !p.IsDeleted);
        var lowStock       = await _db.ProductVariants.CountAsync(v => !v.IsDeleted && v.StockQuantity <= 5);
        var outOfStock     = await _db.ProductVariants.CountAsync(v => !v.IsDeleted && v.StockQuantity == 0);

        var pendingOrders = await _db.Orders
            .CountAsync(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Confirmed);

        var cancelledOrders = await _db.Orders.CountAsync(o => o.Status == OrderStatus.Cancelled);

        var totalWishlistItems = await _db.WishlistItems.CountAsync();

        var avgOrderValue = await _db.Orders
            .Where(o => o.Status != OrderStatus.Cancelled)
            .AverageAsync(o => (decimal?)o.TotalAmount) ?? 0;

        return Ok(new
        {
            revenue = new { today = todayRevenue, week = weekRevenue, month = monthRevenue, total = totalRevenue },
            orders = new { pending = pendingOrders, cancelled = cancelledOrders, avgValue = Math.Round(avgOrderValue, 2) },
            customers = new { today = newCustomersToday, month = newCustomersMonth, total = totalCustomers },
            products = new { total = totalProducts, lowStock, outOfStock },
            wishlist = new { total = totalWishlistItems }
        });
    }
}
