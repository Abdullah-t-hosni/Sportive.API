using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using System.Text;

namespace Sportive.API.Services;

public class OrderService : IOrderService
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notificationService;

    public OrderService(AppDbContext db, INotificationService notificationService)
    {
        _db = db;
        _notificationService = notificationService;
    }

    public async Task<PaginatedResult<OrderSummaryDto>> GetOrdersAsync(
        int page, int pageSize, OrderStatus? status = null, string? search = null, 
        int? customerId = null, DateTime? fromDate = null, DateTime? toDate = null, string? salesPersonId = null)
    {
        var query = _db.Orders
            .Include(o => o.Customer)
            .AsNoTracking();

        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (customerId.HasValue) query = query.Where(o => o.CustomerId == customerId.Value);
        if (fromDate.HasValue) query = query.Where(o => o.CreatedAt >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(o => o.CreatedAt <= toDate.Value);
        if (!string.IsNullOrEmpty(salesPersonId)) query = query.Where(o => o.SalesPersonId == salesPersonId);

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(o => o.OrderNumber.Contains(search) || 
                                     o.Customer.FirstName.Contains(search) || 
                                     o.Customer.LastName.Contains(search));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderSummaryDto(
                o.Id,
                o.OrderNumber,
                o.Customer.FullName,
                o.Customer.Phone ?? "",
                o.Status.ToString(),
                o.FulfillmentType.ToString(),
                o.TotalAmount,
                o.CreatedAt,
                o.Items.Count,
                o.Source.ToString()
            ))
            .ToListAsync();

        return new PaginatedResult<OrderSummaryDto>(items, total, page, pageSize, (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<OrderDetailDto?> GetOrderByIdAsync(int id)
    {
        var o = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.DeliveryAddress)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (o == null) return null;

        var salesPersonName = "";
        if (!string.IsNullOrEmpty(o.SalesPersonId))
        {
            var sp = await _db.Users.FirstOrDefaultAsync(u => u.Id == o.SalesPersonId);
            salesPersonName = sp != null ? $"{sp.FirstName} {sp.LastName}" : "";
        }

        return new OrderDetailDto(
            o.Id,
            o.OrderNumber,
            new CustomerBasicDto(o.Customer.Id, o.Customer.FullName, o.Customer.Email, o.Customer.Phone),
            o.Status.ToString(),
            o.FulfillmentType.ToString(),
            o.PaymentMethod.ToString(),
            o.PaymentStatus.ToString(),
            o.DeliveryAddress != null ? new AddressDto(
                o.DeliveryAddress.Id, o.DeliveryAddress.TitleAr, o.DeliveryAddress.TitleEn,
                o.DeliveryAddress.Street, o.DeliveryAddress.City, o.DeliveryAddress.District,
                o.DeliveryAddress.BuildingNo, o.DeliveryAddress.Floor, o.DeliveryAddress.ApartmentNo,
                o.DeliveryAddress.IsDefault) : null,
            o.PickupScheduledAt,
            o.SubTotal,
            o.DiscountAmount,
            o.DeliveryFee,
            o.TotalAmount,
            o.CustomerNotes,
            o.AdminNotes,
            o.CreatedAt,
            o.Items.Select(i => new OrderItemDto(
                i.Id, i.ProductNameAr, i.ProductNameEn, i.Product.Images.FirstOrDefault(img => img.IsMain)?.ImageUrl ?? "",
                i.Size, i.Color, i.Quantity, i.UnitPrice, i.TotalPrice
            )).ToList(),
            o.StatusHistory.OrderByDescending(h => h.CreatedAt).Select(h => new OrderStatusHistoryDto(
                h.Status.ToString(), h.Note, h.CreatedAt
            )).ToList(),
            salesPersonName,
            null, 0, 0, o.Source.ToString()
        );
    }

    public async Task<PaginatedResult<OrderSummaryDto>> GetCustomerOrdersAsync(int customerId, int page, int pageSize)
    {
        return await GetOrdersAsync(page, pageSize, customerId: customerId);
    }

    public async Task<OrderDetailDto> CreateOrderAsync(int? customerId, CreateOrderDto dto)
    {
        // 1. Handle Customer (For POS or non-logged in website visitors)
        if (!customerId.HasValue && !string.IsNullOrEmpty(dto.CustomerPhone))
        {
            var existing = await _db.Customers.FirstOrDefaultAsync(c => c.Phone == dto.CustomerPhone);
            if (existing != null)
            {
                customerId = existing.Id;
            }
            else
            {
                var names = (dto.CustomerName ?? "Walk-in Guest").Split(' ');
                var c = new Customer
                {
                    FirstName = names[0],
                    LastName = names.Length > 1 ? string.Join(' ', names.Skip(1)) : "Customer",
                    Phone = dto.CustomerPhone,
                    Email = $"{dto.CustomerPhone}@pos.sportive.com", // Dummy email
                    CreatedAt = DateTime.UtcNow
                };
                _db.Customers.Add(c);
                await _db.SaveChangesAsync();
                customerId = c.Id;
            }
        }

        if (!customerId.HasValue) throw new Exception("Customer identification required.");

        // 2. Setup Order
        var order = new Order
        {
            CustomerId = customerId.Value,
            OrderNumber = await GenerateOrderNumberAsync(dto.Source),
            Status = OrderStatus.Pending,
            FulfillmentType = dto.FulfillmentType,
            PaymentMethod = dto.PaymentMethod,
            PaymentStatus = PaymentStatus.Pending,
            DeliveryAddressId = dto.DeliveryAddressId,
            PickupScheduledAt = dto.PickupScheduledAt,
            CustomerNotes = dto.CustomerNotes,
            CouponCode = dto.CouponCode,
            SalesPersonId = dto.SalesPersonId,
            Source = dto.Source,
            CreatedAt = DateTime.UtcNow
        };

        // 3. Handle Items
        if (dto.Items != null && dto.Items.Any())
        {
            foreach (var item in dto.Items)
            {
                var product = await _db.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == item.ProductId);
                if (product == null) continue;

                var variant = item.ProductVariantId.HasValue 
                    ? product.Variants.FirstOrDefault(v => v.Id == item.ProductVariantId)
                    : null;

                var unitPrice = product.DiscountPrice ?? (product.Price + (variant?.PriceAdjustment ?? 0));
                
                var orderItem = new OrderItem
                {
                    ProductId = item.ProductId,
                    ProductVariantId = item.ProductVariantId,
                    ProductNameAr = product.NameAr,
                    ProductNameEn = product.NameEn,
                    Size = variant?.Size,
                    Color = variant?.Color,
                    Quantity = item.Quantity,
                    UnitPrice = unitPrice,
                    TotalPrice = unitPrice * item.Quantity,
                    CreatedAt = DateTime.UtcNow
                };
                order.Items.Add(orderItem);
                order.SubTotal += orderItem.TotalPrice;

                // Simple Stock Update
                if (variant != null) variant.StockQuantity -= item.Quantity;
                else product.TotalStock -= item.Quantity;
            }
        }
        else
        {
            // fallback to Cart if no items provided (Website flow)
            var cartItems = await _db.CartItems.Include(c => c.Product).Include(c => c.ProductVariant)
                .Where(c => c.CustomerId == customerId.Value).ToListAsync();
            
            if (!cartItems.Any()) throw new Exception("Cart is empty.");

            foreach (var ci in cartItems)
            {
                var unitPrice = ci.Product.DiscountPrice ?? (ci.Product.Price + (ci.ProductVariant?.PriceAdjustment ?? 0));
                var orderItem = new OrderItem
                {
                    ProductId = ci.ProductId,
                    ProductVariantId = ci.ProductVariantId,
                    ProductNameAr = ci.Product.NameAr,
                    ProductNameEn = ci.Product.NameEn,
                    Size = ci.ProductVariant?.Size,
                    Color = ci.ProductVariant?.Color,
                    Quantity = ci.Quantity,
                    UnitPrice = unitPrice,
                    TotalPrice = unitPrice * ci.Quantity,
                    CreatedAt = DateTime.UtcNow
                };
                order.Items.Add(orderItem);
                order.SubTotal += orderItem.TotalPrice;

                if (ci.ProductVariant != null) ci.ProductVariant.StockQuantity -= ci.Quantity;
                else ci.Product.TotalStock -= ci.Quantity;
                
                ci.IsDeleted = true; // Clear cart after order
            }
        }

        // 4. Final Totals (Handle Coupons/Fees in real app logic)
        order.TotalAmount = order.SubTotal + order.DeliveryFee - order.DiscountAmount;

        _db.Orders.Add(order);
        
        // Initial History
        order.StatusHistory.Add(new OrderStatusHistory { Status = order.Status, CreatedAt = DateTime.UtcNow, Note = "Order Created." });

        await _db.SaveChangesAsync();

        // 5. Notifications
        var customer = await _db.Customers.FindAsync(order.CustomerId);
        if (customer != null && !string.IsNullOrEmpty(customer.AppUserId))
        {
            await _notificationService.SendAsync(customer.AppUserId, 
                "تم استلام طلبك", "Order Received",
                $"طلبك رقم {order.OrderNumber} قيد الانتظار.", $"Your order #{order.OrderNumber} is pending.",
                "Order", order.Id);
        }

        return (await GetOrderByIdAsync(order.Id))!;
    }

    public async Task<OrderDetailDto> UpdateOrderStatusAsync(int orderId, UpdateOrderStatusDto dto, string updatedByUserId)
    {
        var order = await _db.Orders.Include(o => o.StatusHistory).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw new Exception("Order not found.");

        order.Status = dto.Status;
        order.UpdatedAt = DateTime.UtcNow;
        order.StatusHistory.Add(new OrderStatusHistory {
            Status = dto.Status,
            Note = dto.Note,
            ChangedByUserId = updatedByUserId,
            CreatedAt = DateTime.UtcNow
        });

        if (dto.Status == OrderStatus.Delivered)
        {
            order.ActualDeliveryDate = DateTime.UtcNow;
            order.PaymentStatus = PaymentStatus.Paid;
        }

        await _db.SaveChangesAsync();

        // Notify Customer
        var customer = await _db.Customers.FindAsync(order.CustomerId);
        if (customer != null && !string.IsNullOrEmpty(customer.AppUserId))
        {
            await _notificationService.SendAsync(customer.AppUserId,
                "تحديث حالة الطلب", "Order Status Update",
                $"حالة طلبك {order.OrderNumber} أصبحت: {dto.Status}", $"Order {order.OrderNumber} is now: {dto.Status}",
                "Order", order.Id);
        }

        return (await GetOrderByIdAsync(orderId))!;
    }

    public async Task<string> GenerateOrderNumberAsync(OrderSource source = OrderSource.Website)
    {
        var prefix = source == OrderSource.POS ? "POS" : "ORD";
        var date = DateTime.UtcNow.ToString("yyMMdd");
        var random = new Random().Next(1000, 9999);
        var orderNumber = $"{prefix}-{date}-{random}";
        
        // Ensure uniqueness
        while (await _db.Orders.AnyAsync(o => o.OrderNumber == orderNumber))
        {
            random = new Random().Next(1000, 9999);
            orderNumber = $"{prefix}-{date}-{random}";
        }

        return orderNumber;
    }
}
