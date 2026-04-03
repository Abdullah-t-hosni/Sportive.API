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
    private readonly ICustomerService _customerService;
    private readonly IAccountingService _accounting;

    public OrderService(
        AppDbContext db,
        INotificationService notificationService,
        IEmailService emailService,
        IServiceScopeFactory scopeFactory,
        IInventoryService inventory,
        IConfiguration config,
        ICustomerService customerService,
        IAccountingService accounting)
    {
        _db = db;
        _notificationService = notificationService;
        _emailService = emailService;
        _scopeFactory = scopeFactory;
        _inventory = inventory;
        _config = config;
        _customerService = customerService;
        _accounting = accounting;
    }

    public async Task<PaginatedResult<OrderSummaryDto>> GetOrdersAsync(
        int page, int pageSize, OrderStatus? status = null, string? search = null,
        int? customerId = null, DateTime? fromDate = null, DateTime? toDate = null, string? salesPersonId = null, OrderSource? source = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.Orders
            .Include(o => o.Customer)
            .AsNoTracking();

        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (source.HasValue) query = query.Where(o => o.Source == source.Value);
        if (customerId.HasValue) query = query.Where(o => o.CustomerId == customerId.Value);
        if (fromDate.HasValue) query = query.Where(o => o.CreatedAt >= fromDate.Value.Date);
        if (toDate.HasValue) 
        {
            var endOfDay = toDate.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(o => o.CreatedAt <= endOfDay);
        }
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
                o.PaymentMethod.ToString(),
                o.PaymentStatus.ToString()
            ))
            .ToListAsync();

        return new PaginatedResult<OrderSummaryDto>(items, total, page, pageSize, (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<OrderDetailDto?> GetOrderByIdAsync(int id)
    {
        var o = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.DeliveryAddress)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p.Images)
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
                i.Id, i.ProductNameAr, i.ProductNameEn, i.Product?.Images?.FirstOrDefault(img => img.IsMain)?.ImageUrl ?? "",
                i.Size, i.Color, i.Quantity, i.UnitPrice, i.TotalPrice,
                i.HasTax, i.VatRateApplied, i.ItemVatAmount
            )).ToList(),
            o.StatusHistory.OrderByDescending(h => h.CreatedAt).Select(h => new OrderStatusHistoryDto(
                h.Status.ToString(), h.Note, h.CreatedAt
            )).ToList(),
            salesPersonName,
            null, 0, 0, o.Source.ToString(),
            o.AttachmentUrl, o.AttachmentPublicId
        );
    }

    public async Task<PaginatedResult<OrderSummaryDto>> GetCustomerOrdersAsync(int customerId, int page, int pageSize)
    {
        return await GetOrdersAsync(page, pageSize, customerId: customerId);
    }

    public async Task<OrderDetailDto> CreateOrderAsync(int? customerId, CreateOrderDto dto)
    {
        if (customerId.HasValue && (customerId.Value <= 0 || !await _db.Customers.AnyAsync(c => c.Id == customerId.Value)))
        {
            customerId = null;
        }

        var store = await _db.StoreInfo.FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        
        // 1. Handle Customer
        if (!customerId.HasValue)
        {
            var phone = string.IsNullOrEmpty(dto.CustomerPhone) ? "0000000000" : dto.CustomerPhone;
            var existing = await _db.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Phone == phone);
            
            if (existing != null)
            {
                customerId = existing.Id;
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
                await _customerService.EnsureCustomerAccountAsync(c.Id);
                customerId = c.Id;
            }
        }

        if (!customerId.HasValue) throw new ArgumentException("Customer identification required.");

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
            PaymentStatus = (dto.Source == OrderSource.POS && dto.PaymentMethod != PaymentMethod.Credit) ? PaymentStatus.Paid : PaymentStatus.Pending,
            DeliveryAddressId = (dto.DeliveryAddressId.HasValue && dto.DeliveryAddressId.Value > 0) 
                                ? (await _db.Addresses.AnyAsync(a => a.Id == dto.DeliveryAddressId.Value && a.CustomerId == customerId.Value) ? dto.DeliveryAddressId : null) 
                                : null,
            PickupScheduledAt = dto.PickupScheduledAt,
            CustomerNotes = dto.CustomerNotes,
            CouponCode = dto.CouponCode,
            SalesPersonId = dto.SalesPersonId,
            Source = dto.Source,
            AdminNotes = dto.Note,
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
                    HasTax = item.HasTax ?? product.HasTax,
                    VatRateApplied = item.VatRate ?? product.VatRate ?? (store?.VatRatePercent ?? 14),
                    CreatedAt = DateTime.UtcNow
                };

                if (orderItem.HasTax)
                {
                    var rate = (orderItem.VatRateApplied ?? 14) / 100m;
                    decimal net = Math.Round(orderItem.TotalPrice / (1 + rate), 2);
                    orderItem.ItemVatAmount = orderItem.TotalPrice - net;
                    order.TotalVatAmount += orderItem.ItemVatAmount;
                }

                order.Items.Add(orderItem);
                order.SubTotal += orderItem.TotalPrice;

                await _inventory.LogMovementAsync(
                    InventoryMovementType.Sale,
                    -item.Quantity,
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
                    HasTax = ci.Product.HasTax,
                    VatRateApplied = ci.Product.VatRate ?? (store?.VatRatePercent ?? 14),
                    CreatedAt = DateTime.UtcNow
                };

                if (orderItem.HasTax)
                {
                    var rate = (orderItem.VatRateApplied ?? 14) / 100m;
                    decimal net = Math.Round(orderItem.TotalPrice / (1 + rate), 2);
                    orderItem.ItemVatAmount = orderItem.TotalPrice - net;
                    order.TotalVatAmount += orderItem.ItemVatAmount;
                }

                order.Items.Add(orderItem);
                order.SubTotal += orderItem.TotalPrice;

                await _inventory.LogMovementAsync(
                    InventoryMovementType.Sale,
                    -ci.Quantity,
                    ci.ProductId,
                    ci.ProductVariantId,
                    order.OrderNumber,
                    "Website Order created",
                    null
                );
                
                _db.CartItems.Remove(ci);
            }
        }

        order.TotalAmount = order.SubTotal + order.DeliveryFee - order.DiscountAmount;
        _db.Orders.Add(order);
        order.StatusHistory.Add(new OrderStatusHistory { Status = order.Status, CreatedAt = DateTime.UtcNow, Note = "Order Created." });

        await _db.SaveChangesAsync();

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                var orderInner = await db.Orders
                    .Include(o => o.Customer)
                    .Include(o => o.Items).ThenInclude(i => i.Product)
                    .FirstAsync(o => o.Id == order.Id);

                await accounting.PostSalesOrderAsync(orderInner);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Accounting] PostSalesOrder failed for {order.OrderNumber}: {ex.Message}");
            }
        });

        // 5. Notifications & Email
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var customerInner = await db.Customers.FindAsync(order.CustomerId);
            if (customerInner != null && !string.IsNullOrEmpty(customerInner.AppUserId))
            {
                await notificationService.SendAsync(customerInner.AppUserId, 
                    "تم استلام طلبك", "Order Received",
                    $"طلبك رقم {order.OrderNumber} قيد الانتظار.", $"Your order #{order.OrderNumber} is pending.",
                    "Order", order.Id);
            }

            try 
            {
                var adminEmails = (config["Backup:Email:To"] ?? "admin@sportive.com").Split(',');
                var subject = $"🔔 طلب جديد: {order.OrderNumber}";
                var body = $@"
                    <div dir='rtl' style='font-family: Arial, sans-serif; border: 1px solid #eee; padding: 20px;'>
                        <h2 style='color: #0f3460;'>تنبيه طلب جديد 🆕</h2>
                        <p>رقم الطلب: <b>{order.OrderNumber}</b></p>
                        <p>اسم العميل: {customerInner?.FullName ?? "عميل خارجي"}</p>
                        <p>إجمالي المبلغ: <span style='color: green; font-weight: bold;'>{order.TotalAmount:N2} ج.م</span></p>
                        <hr/>
                        <a href='https://admin.sportive-sportwear.com/orders/{order.Id}' 
                           style='display: inline-block; background: #0f3460; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>
                           عرض التفاصيل
                        </a>
                    </div>";
                foreach(var email in adminEmails) await emailService.SendEmailAsync(email.Trim(), subject, body);
            } catch { }
        });

        return (await GetOrderByIdAsync(order.Id))!;
    }

    public async Task<OrderDetailDto> UpdateOrderStatusAsync(int orderId, UpdateOrderStatusDto dto, string updatedByUserId)
    {
        var order = await _db.Orders.Include(o => o.StatusHistory).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw new KeyNotFoundException("Order not found.");

        var oldStatus = order.Status;
        order.Status = dto.Status;
        order.UpdatedAt = DateTime.UtcNow;
        order.StatusHistory.Add(new OrderStatusHistory {
            Status = dto.Status, Note = dto.Note, ChangedByUserId = updatedByUserId, CreatedAt = DateTime.UtcNow
        });

        if (dto.Status == OrderStatus.Delivered)
        {
            order.ActualDeliveryDate = DateTime.UtcNow;
            if (order.PaymentMethod != PaymentMethod.Credit) order.PaymentStatus = PaymentStatus.Paid;

            if (order.Source == OrderSource.Website && order.PaymentStatus == PaymentStatus.Paid)
            {
                _ = Task.Run(async () =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    try {
                        var fullOrder = await db.Orders.Include(o => o.Customer).FirstAsync(o => o.Id == orderId);
                        await accounting.PostOrderPaymentAsync(fullOrder);
                    } catch { }
                });
            }
        }

        if ((dto.Status == OrderStatus.Cancelled || dto.Status == OrderStatus.Returned) && 
            oldStatus != OrderStatus.Cancelled && oldStatus != OrderStatus.Returned)
        {
            var orderWithItems = await _db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == orderId);
            foreach (var item in orderWithItems.Items)
            {
                await _inventory.LogMovementAsync(
                    dto.Status == OrderStatus.Returned ? InventoryMovementType.ReturnIn : InventoryMovementType.Adjustment,
                    item.Quantity, item.ProductId, item.ProductVariantId, order.OrderNumber, $"Order {dto.Status}", updatedByUserId
                );
            }
        }

        await _db.SaveChangesAsync();

        if (dto.Status == OrderStatus.Returned || dto.Status == OrderStatus.Cancelled)
        {
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                try {
                    var fullOrderInner = await db.Orders.Include(o => o.Customer).Include(o => o.Items).ThenInclude(i => i.Product).FirstAsync(o => o.Id == orderId);
                    await accounting.PostSalesReturnAsync(fullOrderInner);
                    if (fullOrderInner.PaymentStatus == PaymentStatus.Paid) await accounting.PostOrderRefundAsync(fullOrderInner);
                } catch { }
            });
        }

        return (await GetOrderByIdAsync(orderId))!;
    }

    public async Task<string> GenerateOrderNumberAsync(OrderSource source = OrderSource.Website)
    {
        var prefix = source == OrderSource.POS ? "POS" : "ORD";
        var date = DateTime.UtcNow.ToString("yyMMdd");
        string orderNumber;
        do {
            var random = new Random().Next(1000, 9999);
            orderNumber = $"{prefix}-{date}-{random}";
        } while (await _db.Orders.AnyAsync(o => o.OrderNumber == orderNumber));
        return orderNumber;
    }

    public async Task SyncAllOrderAccountingAsync()
    {
        var orders = await _db.Orders.Include(o => o.Customer).Include(o => o.Items).ThenInclude(i => i.Product)
            .Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Pending && !o.IsDeleted).ToListAsync();
        
        foreach (var order in orders)
        {
            try {
                var orderNum = order.OrderNumber.Trim().ToLower();
                var entry = await _db.JournalEntries.Include(j => j.Lines)
                    .FirstOrDefaultAsync(j => !j.IsDeleted && j.Type == JournalEntryType.SalesInvoice && j.Reference != null && j.Reference.Trim().ToLower() == orderNum);

                if (entry == null || !entry.Lines.Any() || entry.Lines.All(l => l.CustomerId == null))
                {
                    if (entry != null) { _db.JournalLines.RemoveRange(entry.Lines); _db.JournalEntries.Remove(entry); await _db.SaveChangesAsync(); }
                    await _accounting.PostSalesOrderAsync(order);
                }
                if (order.PaymentStatus == PaymentStatus.Paid) await _accounting.PostOrderPaymentAsync(order);
            } catch { }
        }
    }
}
