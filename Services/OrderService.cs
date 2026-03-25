using Sportive.API.Utils;
using Microsoft.AspNetCore.Identity;
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
    private readonly UserManager<AppUser> _userManager;

    public OrderService(AppDbContext db, INotificationService notifications, UserManager<AppUser> userManager)
    {
        _db = db;
        _notifications = notifications;
        _userManager = userManager;
    }

    public async Task<PaginatedResult<OrderSummaryDto>> GetOrdersAsync(
        int page, int pageSize, OrderStatus? status = null, string? search = null, int? customerId = null, DateTime? fromDate = null, DateTime? toDate = null, string? salesPersonId = null)
    {
        var query = _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .AsQueryable();

        // Date Filters
        if (fromDate.HasValue)
            query = query.Where(o => o.CreatedAt >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(o => o.CreatedAt <= toDate.Value);

        // Filter by customer (for "my orders")
        if (customerId.HasValue)
            query = query.Where(o => o.CustomerId == customerId.Value);

        if (status.HasValue)
            query = query.Where(o => o.Status == status);

        if (!string.IsNullOrEmpty(salesPersonId))
            query = query.Where(o => o.SalesPersonId == salesPersonId);

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
        return await MapToDetailAsync(o);
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

    public async Task<OrderDetailDto> CreateOrderAsync(int? customerId, CreateOrderDto dto)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        
        return await strategy.ExecuteAsync(async () => {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                int effectiveCustomerId;

                // 1. Handle Customer
                if (customerId.HasValue && customerId.Value > 0)
                {
                    effectiveCustomerId = customerId.Value;
                }
                else if (!string.IsNullOrEmpty(dto.CustomerPhone))
                {
                    // Look for existing customer by phone
                    var customer = await _db.Customers
                        .FirstOrDefaultAsync(c => c.Phone == dto.CustomerPhone && !c.IsDeleted);

                    if (customer == null)
                    {
                        // Create new basic customer for POS
                        var names = (dto.CustomerName ?? "عميل كاشير").Split(' ', 2);
                        customer = new Customer
                        {
                            FirstName = names[0],
                            LastName = names.Length > 1 ? names[1] : string.Empty,
                            Phone = dto.CustomerPhone,
                            Email = $"{dto.CustomerPhone}@sportive.com" // Placeholder
                        };
                        _db.Customers.Add(customer);
                        await _db.SaveChangesAsync();
                    }
                    effectiveCustomerId = customer.Id;
                }
                else
                {
                    // Default to a generic "Cashier Customer" if none provided
                    var defaultCustomer = await _db.Customers
                        .FirstOrDefaultAsync(c => c.Phone == "0000000000" && !c.IsDeleted);
                    
                    if (defaultCustomer == null)
                    {
                        defaultCustomer = new Customer { FirstName = "Walk-in", LastName = "Customer", Phone = "0000000000", Email = "pos@sportive.com" };
                        _db.Customers.Add(defaultCustomer);
                        await _db.SaveChangesAsync();
                    }
                    effectiveCustomerId = defaultCustomer.Id;
                }

                var cartItems = await _db.CartItems
                    .Include(c => c.Product).ThenInclude(p => p.Images)
                    .Include(c => c.ProductVariant)
                    .Where(c => c.CustomerId == effectiveCustomerId && !c.IsDeleted)
                    .ToListAsync();

                // If salla is empty, check if we're creating an order from scratch in POS (not implemented in CartService yet but maybe we should allow it?)
                // Actually, the current system relies on CartItems.

                if (!cartItems.Any())
                    throw new InvalidOperationException("السلة فارغة. يرجى إضافة منتجات أولاً.");

                var order = new Order
                {
                    OrderNumber = await GenerateOrderNumberAsync(dto.Source),
                    CustomerId = effectiveCustomerId,
                    FulfillmentType = dto.FulfillmentType,
                    PaymentMethod = dto.PaymentMethod,
                    DeliveryAddressId = dto.DeliveryAddressId,
                    PickupScheduledAt = dto.PickupScheduledAt,
                    CustomerNotes = dto.CustomerNotes,
                    CouponCode = dto.CouponCode,
                    SalesPersonId = dto.SalesPersonId,
                    Source = dto.Source,
                    Status = string.IsNullOrEmpty(dto.SalesPersonId) ? OrderStatus.Pending : OrderStatus.Delivered,
                    PaymentStatus = string.IsNullOrEmpty(dto.SalesPersonId) ? PaymentStatus.Pending : PaymentStatus.Paid,
                    DeliveryFee = dto.FulfillmentType == FulfillmentType.Delivery ? 50 : 0
                };

                if (!string.IsNullOrEmpty(order.SalesPersonId))
                    order.ActualDeliveryDate = TimeHelper.GetEgyptTime();

                // 1. Calculate Subtotal
                decimal subtotal = 0;
                foreach (var item in cartItems)
                {
                    if (item.Product.Status != ProductStatus.Active)
                        throw new InvalidOperationException($"المنتج {item.Product.NameAr} غير متاح حالياً");

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

                    // 2. Deduct stock
                    if (item.ProductVariant != null)
                    {
                        if (item.ProductVariant.StockQuantity < item.Quantity)
                            throw new InvalidOperationException($"المخزون غير كافٍ للمنتج {item.Product.NameAr} (المقاس: {item.ProductVariant.Size})");
                        
                        item.ProductVariant.StockQuantity -= item.Quantity;

                        // تحديث حالة المنتج إذا نفد المخزون بالكامل
                        var totalStock = item.Product.Variants.Sum(v => v.StockQuantity);
                        if (totalStock <= 0) item.Product.Status = ProductStatus.OutOfStock;

                        // إرسال تنبيه فوري للوحة التحكم
                        await _notifications.BroadcastStockUpdateAsync(item.ProductId, item.ProductVariant.Id, item.ProductVariant.StockQuantity);
                    }
                }

                order.SubTotal = subtotal;

                // 3. Coupon Validation
                if (!string.IsNullOrEmpty(dto.CouponCode))
                {
                    var coupon = await _db.Coupons.FirstOrDefaultAsync(c => 
                        c.Code == dto.CouponCode && c.IsActive && 
                        (!c.ExpiresAt.HasValue || c.ExpiresAt > TimeHelper.GetEgyptTime()));

                    if (coupon != null)
                    {
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
                    Status = order.Status,
                    Note = "تم إنشاء الطلب" + (string.IsNullOrEmpty(order.SalesPersonId) ? "" : " عبر الكاشير")
                });

                _db.Orders.Add(order);
                _db.CartItems.RemoveRange(cartItems);

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                // Broadcast stock updates
                foreach (var item in cartItems)
                {
                    if (item.ProductVariant != null)
                    {
                        await _notifications.BroadcastStockUpdateAsync(item.ProductId, item.ProductVariantId!.Value, item.ProductVariant.StockQuantity);
                    }
                }

                return (await GetOrderByIdAsync(order.Id))!;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    public async Task<OrderDetailDto> UpdateOrderStatusAsync(
        int orderId, UpdateOrderStatusDto dto, string updatedByUserId)
    {
        var order = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items).ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new KeyNotFoundException($"Order {orderId} not found");

        if (order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Returned && 
           (dto.Status == OrderStatus.Cancelled || dto.Status == OrderStatus.Returned))
        {
            foreach (var item in order.Items)
            {
                if (item.ProductVariant != null)
                {
                    item.ProductVariant.StockQuantity += item.Quantity;
                    await _notifications.BroadcastStockUpdateAsync(item.ProductId, item.ProductVariant.Id, item.ProductVariant.StockQuantity);
                }
            }
        }

        order.Status = dto.Status;
        order.UpdatedAt = TimeHelper.GetEgyptTime();

        if (dto.Status == OrderStatus.Delivered)
            order.ActualDeliveryDate = TimeHelper.GetEgyptTime();

        if (dto.Status == OrderStatus.ReadyForPickup)
            order.PickupConfirmedAt = TimeHelper.GetEgyptTime();

        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = orderId,
            Status = dto.Status,
            Note = dto.Note,
            ChangedByUserId = updatedByUserId
        });

        await _db.SaveChangesAsync();

        // 4. Send Notification to Customer
        if (!string.IsNullOrEmpty(order.Customer.AppUserId))
        {
            var statusMsgAr = dto.Status switch {
                OrderStatus.Confirmed => "تم تأكيد طلبك #",
                OrderStatus.Processing => "طلبك قيد التحضير #",
                OrderStatus.ReadyForPickup => "طلبك جاهز للاستلام #",
                OrderStatus.OutForDelivery => "طلبك في الطريق إليك #",
                OrderStatus.Delivered => "تم توصيل طلبك بنجاح #",
                OrderStatus.Cancelled => "تم إلغاء طلبك #",
                OrderStatus.Returned => "طلبك مرتجع #",
                _ => "تحديث جديد لطلبك #"
            } + order.OrderNumber;

            var statusMsgEn = dto.Status switch {
                OrderStatus.Confirmed => "Your order has been confirmed #",
                OrderStatus.Processing => "Your order is being processed #",
                OrderStatus.ReadyForPickup => "Your order is ready for pickup #",
                OrderStatus.OutForDelivery => "Your order is out for delivery #",
                OrderStatus.Delivered => "Your order has been delivered #",
                OrderStatus.Cancelled => "Your order has been cancelled #",
                OrderStatus.Returned => "Your order has been returned #",
                _ => "New update for your order #"
            } + order.OrderNumber;

            await _notifications.SendAsync(
                order.Customer.AppUserId,
                "تحديث حالة الطلب", "Order Status Update",
                statusMsgAr, statusMsgEn,
                "OrderUpdate", order.Id);
        }

        return (await GetOrderByIdAsync(orderId))!;
    }

    public async Task<string> GenerateOrderNumberAsync(OrderSource source = OrderSource.Website)
    {
        if (source == OrderSource.POS)
        {
            var yearStart = new DateTime(DateTime.UtcNow.Year, 1, 1);
            var yearEnd   = yearStart.AddYears(1);
            var posCount  = await _db.Orders.IgnoreQueryFilters()
                .CountAsync(o => o.Source == OrderSource.POS
                              && o.CreatedAt >= yearStart
                              && o.CreatedAt < yearEnd);
            var year = DateTime.UtcNow.Year % 100;
            return $"{year}{(posCount + 1):D4}";
        }

        var today    = TimeHelper.GetEgyptTime();
        var dayStart = today.Date;
        var dayEnd   = dayStart.AddDays(1);
        var count    = await _db.Orders.IgnoreQueryFilters()
            .CountAsync(o => o.Source == OrderSource.Website
                          && o.CreatedAt >= dayStart
                          && o.CreatedAt < dayEnd);
        return $"SZ-{today:yyyyMMdd}-{(count + 1):D4}";
    }

    private string NumberToArabicWords(decimal number)
    {
        try { return CurrencyHelper.ToArabicWords(number); }
        catch { return $"{number} EGP"; }
    }

    private async Task<OrderDetailDto> MapToDetailAsync(Order o) 
    {
        string? sellerName = null;
        if (!string.IsNullOrEmpty(o.SalesPersonId))
        {
            var seller = await _userManager.FindByIdAsync(o.SalesPersonId);
            if (seller != null) sellerName = $"{seller.FirstName} {seller.LastName}";
        }

        decimal previousBalance = 0;

        return new OrderDetailDto(
            o.Id, o.OrderNumber,
            new CustomerBasicDto(o.Customer.Id, $"{o.Customer.FirstName} {o.Customer.LastName}", o.Customer.Email, o.Customer.Phone),
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
                .ToList(),
            sellerName,
            NumberToArabicWords(o.TotalAmount),
            previousBalance,
            o.PaymentStatus == PaymentStatus.Paid ? o.TotalAmount : 0
        );
    }
}
