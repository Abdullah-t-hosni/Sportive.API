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
    private readonly IEmailService _emailService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IInventoryService _inventory;
    private readonly IConfiguration _config;

    public OrderService(
        AppDbContext db,
        INotificationService notificationService,
        IEmailService emailService,
        IServiceScopeFactory scopeFactory,
        IInventoryService inventory,
        IConfiguration config)
    {
        _db = db;
        _notificationService = notificationService;
        _emailService = emailService;
        _scopeFactory = scopeFactory;
        _inventory = inventory;
        _config = config;
    }

    public async Task<PaginatedResult<OrderSummaryDto>> GetOrdersAsync(
        int page, int pageSize, OrderStatus? status = null, string? search = null, 
        int? customerId = null, DateTime? fromDate = null, DateTime? toDate = null, string? salesPersonId = null, int? source = null)
    {
        var query = _db.Orders
            .Include(o => o.Customer)
            .AsNoTracking();

        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (source.HasValue) query = query.Where(o => o.Source == (OrderSource)source.Value);
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
                o.Source.ToString(),
                o.PaymentMethod.ToString()
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
            null, 0, 0, (int)o.Source,
            o.AttachmentUrl, o.AttachmentPublicId
        );
    }

    public async Task<PaginatedResult<OrderSummaryDto>> GetCustomerOrdersAsync(int customerId, int page, int pageSize)
    {
        return await GetOrdersAsync(page, pageSize, customerId: customerId);
    }

    public async Task<OrderDetailDto> CreateOrderAsync(int? customerId, CreateOrderDto dto)
    {
        // 🛡️ SECURITY & INTEGRITY FIX: Handle customerId = 0 or invalid ID
        if (customerId.HasValue && (customerId.Value <= 0 || !await _db.Customers.AnyAsync(c => c.Id == customerId.Value)))
        {
            customerId = null;
        }

        // 1. Handle Customer (For POS or non-logged in website visitors)
        if (!customerId.HasValue)
        {
            var phone = string.IsNullOrEmpty(dto.CustomerPhone) ? "0000000000" : dto.CustomerPhone;
            
            // Use IgnoreQueryFilters to find soft-deleted customers
            var existing = await _db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Phone == phone);
            
            if (existing != null)
            {
                customerId = existing.Id;
                // If it was deleted, restore it
                if (existing.IsDeleted)
                {
                    existing.IsDeleted = false;
                    existing.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }
            else
            {
                var names = (dto.CustomerName ?? "Walking Customer").Split(' ');
                var c = new Customer
                {
                    FirstName = names[0],
                    LastName = names.Length > 1 ? string.Join(' ', names.Skip(1)) : "Customer",
                    Phone = phone,
                    Email = $"{phone}@pos.sportive.com",
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };
                _db.Customers.Add(c);
                await _db.SaveChangesAsync();
                customerId = c.Id;
            }
        }

        if (!customerId.HasValue) throw new ArgumentException("Customer identification required.");

        // 🛡️ POS PROTECTION: Ensure SalesPersonId is provided for POS orders
        if (dto.Source == OrderSource.POS && string.IsNullOrEmpty(dto.SalesPersonId))
        {
            throw new ArgumentException("معرف البائع مطلوب لعمليات الـ POS");
        }

        // 2. Setup Order
        var order = new Order
        {
            CustomerId = customerId.Value,
            OrderNumber = await GenerateOrderNumberAsync(dto.Source),
            Status = dto.Source == OrderSource.POS ? OrderStatus.Delivered : OrderStatus.Pending,
            FulfillmentType = dto.FulfillmentType,
            PaymentMethod = dto.PaymentMethod,
            PaymentStatus = dto.Source == OrderSource.POS ? PaymentStatus.Paid : PaymentStatus.Pending,
            DeliveryAddressId = (dto.DeliveryAddressId.HasValue && dto.DeliveryAddressId.Value > 0) 
                                ? (await _db.Addresses.AnyAsync(a => a.Id == dto.DeliveryAddressId.Value && a.CustomerId == customerId.Value) ? dto.DeliveryAddressId : null) 
                                : null,
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

                // Stock Update via InventoryService
                await _inventory.LogMovementAsync(
                    InventoryMovementType.Sale,
                    -item.Quantity, // deduction
                    item.ProductId,
                    item.ProductVariantId,
                    order.OrderNumber,
                    "Order created",
                    order.SalesPersonId
                );
            }
        }
        else
        {
            // fallback to Cart if no items provided (Website flow)
            var cartItems = await _db.CartItems.Include(c => c.Product).Include(c => c.ProductVariant)
                .Where(c => c.CustomerId == customerId.Value).ToListAsync();
            
            if (!cartItems.Any()) throw new ArgumentException("Cart is empty.");

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

                // Stock Update via InventoryService
                await _inventory.LogMovementAsync(
                    InventoryMovementType.Sale,
                    -ci.Quantity, // deduction
                    ci.ProductId,
                    ci.ProductVariantId,
                    order.OrderNumber,
                    "Website Order created",
                    null
                );
                
                ci.IsDeleted = true; // Clear cart after order
            }
        }

        // 4. Final Totals (Handle Coupons/Fees in real app logic)
        order.TotalAmount = order.SubTotal + order.DeliveryFee - order.DiscountAmount;

        _db.Orders.Add(order);
        
        // Initial History
        order.StatusHistory.Add(new OrderStatusHistory { Status = order.Status, CreatedAt = DateTime.UtcNow, Note = "Order Created." });

        await _db.SaveChangesAsync();

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                // Re-fetch the order inside the new scope to avoid context mismatch
                var orderInner = await db.Orders
                    .Include(o => o.Customer)
                    .Include(o => o.Items).ThenInclude(i => i.Product)
                    .FirstAsync(o => o.Id == order.Id);

                await accounting.PostSalesOrderAsync(orderInner);

                // 🛡️ REFINEMENT: Immediate collection for POS
                if (orderInner.Source == OrderSource.POS)
                {
                    await accounting.PostOrderPaymentAsync(orderInner);
                }
            }
            catch (Exception ex)
            {
                // لا نوقف الطلب بسبب خطأ محاسبي — نسجله فقط
                Console.Error.WriteLine($"[Accounting] PostSalesOrder failed for {order.OrderNumber}: {ex.Message}");
            }
        });

        // 5. Notifications
        var customer = await _db.Customers.FindAsync(order.CustomerId);
        if (customer != null && !string.IsNullOrEmpty(customer.AppUserId))
        {
            await _notificationService.SendAsync(customer.AppUserId, 
                "تم استلام طلبك", "Order Received",
                $"طلبك رقم {order.OrderNumber} قيد الانتظار.", $"Your order #{order.OrderNumber} is pending.",
                "Order", order.Id);
        }

        // 🛡️ SECURITY & ADMIN NOTIFICATION: Send email to Admin
        _ = Task.Run(async () =>
        {
            try 
            {
                var adminEmails = _config["Backup:Email:To"]?.Split(',') ?? new[] { "admin@sportive.com" };
                var subject = $"🔔 طلب جديد: {order.OrderNumber}";
                var body = $@"
                    <div dir='rtl' style='font-family: Arial, sans-serif; border: 1px solid #eee; padding: 20px;'>
                        <h2 style='color: #0f3460;'>تنبيه طلب جديد 🆕</h2>
                        <p>رقم الطلب: <b>{order.OrderNumber}</b></p>
                        <p>اسم العميل: {customer?.FullName ?? "عميل خارجي"}</p>
                        <p>إجمالي المبلغ: <span style='color: green; font-weight: bold;'>{order.TotalAmount:N2} ج.م</span></p>
                        <p>رقم التليفون: {customer?.Phone ?? "غير مسجل"}</p>
                        <hr/>
                        <a href='https://admin.sportive-sportwear.com/orders/{order.Id}' 
                           style='display: inline-block; background: #0f3460; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>
                           عرض تفاصيل الطلب في لوحة التحكم
                        </a>
                    </div>";
                
                foreach(var email in adminEmails)
                {
                    await _emailService.SendEmailAsync(email.Trim(), subject, body);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Admin Notification] Failed: {ex.Message}");
            }
        });

        return (await GetOrderByIdAsync(order.Id))!;
    }

    public async Task<OrderDetailDto> UpdateOrderStatusAsync(int orderId, UpdateOrderStatusDto dto, string updatedByUserId)
    {
        var order = await _db.Orders.Include(o => o.StatusHistory).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw new KeyNotFoundException("Order not found.");

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

            // أتمتة التحصيل المحاسبي لطلبات الموقع (لأن الـ POS سدد في قيد الفاتورة الأول)
            if (order.Source == OrderSource.Website)
            {
                _ = Task.Run(async () =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    try
                    {
                        var fullOrder = await db.Orders.Include(o => o.Customer).FirstAsync(o => o.Id == orderId);
                        await accounting.PostOrderPaymentAsync(fullOrder);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[Accounting] Auto-collection failed for {orderId}: {ex.Message}");
                    }
                });
            }
        }

        // Stock Restoration for Cancelled/Returned
        if ((dto.Status == OrderStatus.Cancelled || dto.Status == OrderStatus.Returned) && 
            order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Returned)
        {
            var orderWithItems = await _db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == orderId);
            foreach (var item in orderWithItems.Items)
            {
                await _inventory.LogMovementAsync(
                    dto.Status == OrderStatus.Returned ? InventoryMovementType.ReturnIn : InventoryMovementType.Adjustment,
                    item.Quantity, // Add back
                    item.ProductId,
                    item.ProductVariantId,
                    order.OrderNumber,
                    $"Order {dto.Status}",
                    updatedByUserId
                );
            }
        }

        await _db.SaveChangesAsync();

        if (dto.Status == OrderStatus.Returned || dto.Status == OrderStatus.Cancelled)
        {
            var fullOrder = await _db.Orders
                .Include(o => o.Customer)
                .FirstAsync(o => o.Id == orderId);

            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                try
                {
                    // Re-fetch the order inside the new scope to avoid context mismatch
                    var fullOrderInner = await db.Orders
                        .Include(o => o.Customer)
                        .Include(o => o.Items).ThenInclude(i => i.Product)
                        .FirstAsync(o => o.Id == orderId);

                    await accounting.PostSalesReturnAsync(fullOrderInner);

                    // 🛡️ REFINEMENT: If previously PAID, we must issue a REFUND entry
                    if (fullOrderInner.PaymentStatus == PaymentStatus.Paid)
                    {
                        await accounting.PostOrderRefundAsync(fullOrderInner);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Accounting] PostSalesReturn/Refund failed for {orderId}: {ex.Message}");
                }
            });
        }

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
