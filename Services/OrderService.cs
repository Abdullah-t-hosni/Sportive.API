using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public class OrderService : IOrderService
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;

    public OrderService(AppDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task<PaginatedResult<OrderSummaryDto>> GetOrdersAsync(
        int page, int pageSize, OrderStatus? status = null, string? search = null, int? customerId = null)
    {
        var query = _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .AsQueryable();

        // Filter by customer (for "my orders")
        if (customerId.HasValue)
            query = query.Where(o => o.CustomerId == customerId.Value);

        if (status.HasValue)
            query = query.Where(o => o.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(o =>
                o.OrderNumber.ToLower().Contains(s) ||
                o.Customer.FirstName.ToLower().Contains(s) ||
                o.Customer.LastName.ToLower().Contains(s) ||
                (o.Customer.Phone != null && o.Customer.Phone.Contains(s)));
        }

        query = query.OrderByDescending(o => o.CreatedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderSummaryDto(
                o.Id, o.OrderNumber,
                o.Customer.FirstName + " " + o.Customer.LastName,
                o.Customer.Phone ?? "",
                o.Status.ToString(),
                o.FulfillmentType.ToString(),
                o.TotalAmount,
                o.CreatedAt,
                o.Items.Sum(i => i.Quantity)
            ))
            .ToListAsync();

        return new PaginatedResult<OrderSummaryDto>(
            items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize));
    }

    public async Task<OrderDetailDto?> GetOrderByIdAsync(int id)
    {
        var o = await _db.Orders
            .Include(x => x.Customer).ThenInclude(c => c.Addresses)
            .Include(x => x.DeliveryAddress)
            .Include(x => x.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Images)
            .Include(x => x.StatusHistory)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (o == null) return null;
        return MapToDetail(o);
    }

    public async Task<PaginatedResult<OrderSummaryDto>> GetCustomerOrdersAsync(
        int customerId, int page, int pageSize)
    {
        var query = _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderSummaryDto(
                o.Id, o.OrderNumber,
                o.Customer.FirstName + " " + o.Customer.LastName,
                o.Customer.Phone ?? "",
                o.Status.ToString(),
                o.FulfillmentType.ToString(),
                o.TotalAmount,
                o.CreatedAt,
                o.Items.Sum(i => i.Quantity)
            ))
            .ToListAsync();

        return new PaginatedResult<OrderSummaryDto>(
            items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize));
    }

    public async Task<OrderDetailDto> CreateOrderAsync(int customerId, CreateOrderDto dto)
    {
        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var cartItems = await _db.CartItems
                .Include(c => c.Product).ThenInclude(p => p.Images)
                .Include(c => c.ProductVariant)
                .Where(c => c.CustomerId == customerId)
                .ToListAsync();

            if (!cartItems.Any())
                throw new InvalidOperationException("السلة فارغة");

            var order = new Order
            {
                OrderNumber = await GenerateOrderNumberAsync(),
                CustomerId = customerId,
                FulfillmentType = dto.FulfillmentType,
                PaymentMethod = dto.PaymentMethod,
                DeliveryAddressId = dto.DeliveryAddressId,
                PickupScheduledAt = dto.PickupScheduledAt,
                CustomerNotes = dto.CustomerNotes,
                CouponCode = dto.CouponCode,
                Status = OrderStatus.Pending,
                DeliveryFee = dto.FulfillmentType == FulfillmentType.Delivery ? 50 : 0
            };

            // 1. Calculate Subtotal
            decimal subtotal = 0;
            foreach (var item in cartItems)
            {
                var effectivePrice = item.Product.DiscountPrice ?? item.Product.Price;
                if (item.ProductVariant?.PriceAdjustment.HasValue == true)
                    effectivePrice += item.ProductVariant.PriceAdjustment!.Value;

                var orderItem = new OrderItem
                {
                    ProductId = item.ProductId,
                    ProductVariantId = item.ProductVariantId,
                    ProductNameAr = item.Product.NameAr,
                    ProductNameEn = item.Product.NameEn,
                    Size = item.ProductVariant?.Size,
                    Color = item.ProductVariant?.Color,
                    Quantity = item.Quantity,
                    UnitPrice = effectivePrice,
                    TotalPrice = effectivePrice * item.Quantity
                };
                order.Items.Add(orderItem);
                subtotal += orderItem.TotalPrice;

                // 2. Deduct stock and check availability
                if (item.ProductVariant != null)
                {
                    if (item.ProductVariant.StockQuantity < item.Quantity)
                        throw new InvalidOperationException($"الكمية المطلوبة من {item.Product.NameAr} غير متوفرة");
                    
                    item.ProductVariant.StockQuantity -= item.Quantity;
                }
            }

            order.SubTotal = subtotal;

            // 3. Server-side Coupon Validation (CRITICAL)
            if (!string.IsNullOrEmpty(dto.CouponCode))
            {
                var coupon = await _db.Coupons.FirstOrDefaultAsync(c => 
                    c.Code == dto.CouponCode && c.IsActive && 
                    (!c.ExpiresAt.HasValue || c.ExpiresAt > DateTime.UtcNow));

                if (coupon != null)
                {
                    // Basic validation logic (simplified from ICouponService)
                    if (!(coupon.MaxUsageCount.HasValue && coupon.CurrentUsageCount >= coupon.MaxUsageCount) &&
                        !(coupon.MinOrderAmount.HasValue && subtotal < coupon.MinOrderAmount))
                    {
                        decimal disc = coupon.DiscountType == DiscountType.Percentage 
                            ? subtotal * (coupon.DiscountValue / 100) 
                            : coupon.DiscountValue;

                        if (coupon.MaxDiscountAmount.HasValue && coupon.MaxDiscountAmount > 0)
                            disc = Math.Min(disc, coupon.MaxDiscountAmount.Value);

                        order.DiscountAmount = Math.Round(disc, 2);
                        coupon.CurrentUsageCount++;
                    }
                }
            }

            order.TotalAmount = subtotal + order.DeliveryFee - order.DiscountAmount;

            order.StatusHistory.Add(new OrderStatusHistory
            {
                Status = OrderStatus.Pending,
                Note = "تم استلام الطلب وبانتظار الإجراء"
            });

            _db.Orders.Add(order);
            _db.CartItems.RemoveRange(cartItems);

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return (await GetOrderByIdAsync(order.Id))!;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<OrderDetailDto> UpdateOrderStatusAsync(
        int orderId, UpdateOrderStatusDto dto, string updatedByUserId)
    {
        var order = await _db.Orders
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new KeyNotFoundException($"Order {orderId} not found");

        order.Status = dto.Status;
        order.UpdatedAt = DateTime.UtcNow;

        if (dto.Status == OrderStatus.Delivered)
            order.ActualDeliveryDate = DateTime.UtcNow;

        if (dto.Status == OrderStatus.ReadyForPickup)
            order.PickupConfirmedAt = DateTime.UtcNow;

        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = orderId,
            Status = dto.Status,
            Note = dto.Note,
            ChangedByUserId = updatedByUserId
        });

        await _db.SaveChangesAsync();

        // Send Auto Notification
        string titleAr = "تحديث حالة الطلب";
        string titleEn = "Order Status Update";
        string msgAr = $"تم تغيير حالة طلبك رقم {order.OrderNumber} إلى: {dto.Status}";
        string msgEn = $"Your order {order.OrderNumber} status has been updated to: {dto.Status}";
        
        await _notifications.SendAsync(order.Customer?.AppUserId, titleAr, titleEn, msgAr, msgEn, "OrderUpdate", order.Id);

        return (await GetOrderByIdAsync(orderId))!;
    }

    public async Task<string> GenerateOrderNumberAsync()
    {
        var today    = DateTime.UtcNow;
        var dayStart = today.Date;
        var dayEnd   = dayStart.AddDays(1);

        var count = await _db.Orders.CountAsync(o =>
            o.CreatedAt >= dayStart && o.CreatedAt < dayEnd);

        return $"SZ-{today:yyyyMMdd}-{(count + 1):D4}";
    }

    private static OrderDetailDto MapToDetail(Order o) => new(
        o.Id, o.OrderNumber,
        new CustomerBasicDto(o.Customer.Id, o.Customer.FullName, o.Customer.Email, o.Customer.Phone),
        o.Status.ToString(), o.FulfillmentType.ToString(),
        o.PaymentMethod.ToString(), o.PaymentStatus.ToString(),
        o.DeliveryAddress == null ? null : new AddressDto(
            o.DeliveryAddress.Id,
            o.DeliveryAddress.TitleAr, o.DeliveryAddress.TitleEn,
            o.DeliveryAddress.Street, o.DeliveryAddress.City,
            o.DeliveryAddress.District, o.DeliveryAddress.BuildingNo,
            o.DeliveryAddress.Floor, o.DeliveryAddress.ApartmentNo,
            o.DeliveryAddress.IsDefault
        ),
        o.PickupScheduledAt, o.SubTotal, o.DiscountAmount, o.DeliveryFee, o.TotalAmount,
        o.CustomerNotes, o.AdminNotes, o.CreatedAt,
        o.Items.Select(i => new OrderItemDto(
            i.Id, i.ProductNameAr, i.ProductNameEn,
            i.Product?.Images.FirstOrDefault(img => img.IsMain)?.ImageUrl,
            i.Size, i.Color, i.Quantity, i.UnitPrice, i.TotalPrice
        )).ToList(),
        o.StatusHistory.OrderByDescending(h => h.CreatedAt)
            .Select(h => new OrderStatusHistoryDto(h.Status.ToString(), h.Note, h.CreatedAt))
            .ToList()
    );
}
