using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public class OrderService : IOrderService
{
    private readonly AppDbContext _db;
    private readonly ICouponService _coupons;

    public OrderService(AppDbContext db, ICouponService coupons)
    {
        _db = db;
        _coupons = coupons;
    }

    public async Task<PaginatedResult<OrderSummaryDto>> GetOrdersAsync(
        int page, int pageSize, OrderStatus? status = null, string? search = null)
    {
        var query = _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .AsQueryable();

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
        var cartItems = await _db.CartItems
            .Include(c => c.Product).ThenInclude(p => p.Images)
            .Include(c => c.ProductVariant)
            .Where(c => c.CustomerId == customerId)
            .ToListAsync();

        if (!cartItems.Any())
            throw new InvalidOperationException(
                "Cart is empty for this customer. Add items using the same customer id as your account (from login: customerId), " +
                "or call POST /api/orders without ?customerId= so the server uses your profile.");

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
            DeliveryFee = dto.FulfillmentType == FulfillmentType.Delivery ? 50 : 0 // 50 EGP delivery fee
        };

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

            // Deduct stock
            if (item.ProductVariant != null)
                item.ProductVariant.StockQuantity -= item.Quantity;
        }

        order.SubTotal = subtotal;

        if (!string.IsNullOrWhiteSpace(dto.CouponCode))
        {
            var (valid, discount, error) = await _coupons.ValidateAsync(dto.CouponCode, subtotal);
            if (!valid)
                throw new InvalidOperationException(error ?? "كود الخصم غير صحيح");

            order.DiscountAmount = discount;
            await _coupons.UseAsync(dto.CouponCode);
        }

        order.TotalAmount = subtotal + order.DeliveryFee - order.DiscountAmount;

        order.StatusHistory.Add(new OrderStatusHistory
        {
            Status = OrderStatus.Pending,
            Note = "Order placed"
        });

        _db.Orders.Add(order);

        // Clear cart
        _db.CartItems.RemoveRange(cartItems);

        await _db.SaveChangesAsync();
        return (await GetOrderByIdAsync(order.Id))!;
    }

    public async Task<OrderDetailDto> UpdateOrderStatusAsync(
        int orderId, UpdateOrderStatusDto dto, string updatedByUserId)
    {
        var order = await _db.Orders.FindAsync(orderId)
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
        return (await GetOrderByIdAsync(orderId))!;
    }

    public async Task<string> GenerateOrderNumberAsync()
    {
        var today = DateTime.UtcNow;
        var count = await _db.Orders.CountAsync(o =>
            o.CreatedAt.Year == today.Year &&
            o.CreatedAt.Month == today.Month &&
            o.CreatedAt.Day == today.Day);

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
