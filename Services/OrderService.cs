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
        int? customerId = null, DateTime? fromDate = null, DateTime? toDate = null, string? salesPersonId = null, OrderSource? source = null, PaymentMethod? paymentMethod = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.Orders
            .Include(o => o.Customer)
            .AsNoTracking();

        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (source.HasValue) query = query.Where(o => o.Source == source.Value);
        if (paymentMethod.HasValue) query = query.Where(o => o.PaymentMethod == paymentMethod.Value);
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
                                     o.Customer.FullName.Contains(search));
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
                o.PaymentStatus.ToString(),
                o.AdminNotes
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
            salesPersonName = sp?.FullName ?? "";
        }

        // 💡 SMART FINANCE: Calculate actual paid amount from Journal Entries
        var paidAmount = await _db.JournalLines
            .Where(l => l.OrderId == id && l.Credit > 0)
            .Where(l => l.Account.Code.StartsWith("1103")) // Credit to Receivables = Money Paid
            .SumAsync(l => l.Credit);

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
            null, 
            0, 
            paidAmount, // ✅ Show the real paid amount
            o.Source.ToString(),
            o.AttachmentUrl, 
            o.AttachmentPublicId
        );
    }

    public async Task<PaginatedResult<OrderSummaryDto>> GetCustomerOrdersAsync(int customerId, int page, int pageSize)
    {
        return await GetOrdersAsync(page, pageSize, customerId: customerId, paymentMethod: null);
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
                var c = new Customer
                {
                    FullName = dto.CustomerName ?? "Walk-in Customer",
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

        var actualSource = (int)dto.Source == 0 ? OrderSource.Website : dto.Source;
        var order = new Order
        {
            CustomerId = customerId.Value,
            OrderNumber = await GenerateOrderNumberAsync(actualSource),
            Status = actualSource == OrderSource.POS ? OrderStatus.Delivered : OrderStatus.Pending,
            FulfillmentType = dto.FulfillmentType,
            PaymentMethod = dto.PaymentMethod,
            PaymentStatus = (actualSource == OrderSource.POS && dto.PaymentMethod != PaymentMethod.Credit) ? PaymentStatus.Paid : PaymentStatus.Pending,
            DeliveryAddressId = (dto.DeliveryAddressId.HasValue && dto.DeliveryAddressId.Value > 0) 
                                ? (await _db.Addresses.AnyAsync(a => a.Id == dto.DeliveryAddressId.Value && a.CustomerId == customerId.Value) ? dto.DeliveryAddressId : null) 
                                : null,
            PickupScheduledAt = dto.PickupScheduledAt,
            CustomerNotes = dto.CustomerNotes,
            CouponCode = dto.CouponCode,
            SalesPersonId = dto.SalesPersonId,
            Source = actualSource,
            AdminNotes = dto.Note,
            DiscountAmount = dto.DiscountAmount ?? 0,
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

                // For POS, we trust the price sent by the cashier (which may include manual discounts)
                var unitPrice = (actualSource == OrderSource.POS) 
                    ? item.UnitPrice 
                    : (product.DiscountPrice ?? (product.Price + (variant?.PriceAdjustment ?? 0)));
                
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
                    TotalPrice = (actualSource == OrderSource.POS) ? item.TotalPrice : (unitPrice * item.Quantity),
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

                // 🛑 STOCK VALIDATION
                var availableStock = variant?.StockQuantity ?? product.TotalStock;
                if (item.Quantity > availableStock)
                {
                    throw new ArgumentException(
                        actualSource == OrderSource.POS
                        ? $"الكمية المطلوبة ({item.Quantity}) من {product.NameAr} غير متاحة في المخزون (المتاح: {availableStock})"
                        : $"Requested quantity for {product.NameAr} is not available in stock.");
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
            var cartItems = await _db.CartItems.Include(c => c.Product).ThenInclude(p => p.Variants)
                .Include(c => c.ProductVariant)
                .Where(c => c.CustomerId == customerId.Value).ToListAsync();
            
            if (!cartItems.Any()) throw new ArgumentException("Cart is empty.");

            foreach (var ci in cartItems)
            {
                // 🛑 STOCK VALIDATION (Website)
                var availableStock = ci.ProductVariant?.StockQuantity ?? ci.Product.TotalStock;
                if (ci.Quantity > availableStock)
                {
                    throw new ArgumentException($"الكمية المطلوبة ({ci.Quantity}) من {ci.Product.NameAr} غير متاحة في المخزون حالياً.");
                }

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

        // 4. Finalize Totals & Delivery (Website Specific)
        if (order.Source == OrderSource.Website && order.FulfillmentType == FulfillmentType.Delivery)
        {
            // Fetch delivery fee from store settings
            order.DeliveryFee = (order.SubTotal >= (store?.FreeDeliveryAt ?? 2000)) ? 0 : (store?.FixedDeliveryFee ?? 50);
        }

        order.TotalAmount = order.SubTotal + order.DeliveryFee - order.DiscountAmount;
        _db.Orders.Add(order);
        order.StatusHistory.Add(new OrderStatusHistory { Status = order.Status, CreatedAt = DateTime.UtcNow, Note = "Order Created." });

        await _db.SaveChangesAsync();

        _ = Task.Run(async () =>
        {
            if (order.Source == OrderSource.POS) return; // ✅ Delegation: POS accounting is handled by Frontend for better precision

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

        // 💡 AUTO-FLOW: For Website orders, Digital Payments (Vodafone, InstaPay, Visa) are paid upon Confirmation. Cash is paid ONLY upon Delivery.
        if (order.Source == OrderSource.Website && dto.Status == OrderStatus.Confirmed)
        {
            if (order.PaymentMethod == PaymentMethod.Vodafone || 
                order.PaymentMethod == PaymentMethod.InstaPay || 
                order.PaymentMethod == PaymentMethod.CreditCard)
            {
                order.PaymentStatus = PaymentStatus.Paid;
            }
        }

        if (dto.Status == OrderStatus.Delivered)
        {
            order.ActualDeliveryDate = DateTime.UtcNow;
            if (order.PaymentMethod != PaymentMethod.Credit) order.PaymentStatus = PaymentStatus.Paid;
        }

        if (dto.Status == OrderStatus.Returned && order.Source == OrderSource.POS && order.PaymentStatus == PaymentStatus.Paid)
        {
            using var scope = _scopeFactory.CreateScope();
            var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
            var cashCode = await accounting.GetMappedCashAccount(order.PaymentMethod, order.Source);
            
            // ✅ نتحقق من رصيد الدرج في اليوم الحالي فقط (بناءً على طلب العميل للحسابات المنفصلة يومياً)
            var todayBalance = await accounting.GetTodayDrawerBalanceAsync(cashCode);
            if (todayBalance < order.TotalAmount)
            {
               throw new InvalidOperationException($"عذراً، رصيد الدرج لليوم ({todayBalance:N2} ج.م) غير كافٍ لرد نقود هذا الطلب ({order.TotalAmount:N2} ج.م).");
            }

            // Mark all items as returned
            var orderWithItems = await _db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == orderId);
            foreach (var it in orderWithItems.Items) it.ReturnedQuantity = it.Quantity;
        }

        // Post accounting if paid
        if ((dto.Status == OrderStatus.Delivered || dto.Status == OrderStatus.Confirmed) && 
            order.Source == OrderSource.Website && order.PaymentStatus == PaymentStatus.Paid)
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
                } catch { }
            });
        }

        return (await GetOrderByIdAsync(orderId))!;
    }

    public async Task<OrderDetailDto> ProcessPartialReturnAsync(int orderId, PartialReturnDto dto, string updatedByUserId)
    {
        var order = await _db.Orders.Include(o => o.Items).ThenInclude(i => i.Product).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw new KeyNotFoundException("Order not found.");
        if (order.Status == OrderStatus.Cancelled) throw new InvalidOperationException("Cannot return items from a cancelled order.");

        // 1. Calculate Refund Amount (Proportional to Discount) First
        decimal refundAmountTotal = 0;
        var discountRatio = order.SubTotal > 0 ? (order.TotalAmount - order.DeliveryFee) / order.SubTotal : 1;

        foreach (var req in dto.Items)
        {
            var line = order.Items.FirstOrDefault(i => i.Id == req.OrderItemId);
            if (line != null && req.Quantity > 0)
            {
                // We refund the proportional part of what they PAID (Price after discount share)
                var grossPrice = line.UnitPrice * req.Quantity;
                refundAmountTotal += Math.Round(grossPrice * discountRatio, 2);
            }
        }

        // 2. Mandatory Drawer Balance Check (only for PAID POS returns)
        if (order.Source == OrderSource.POS && order.PaymentStatus == PaymentStatus.Paid)
        {
            using var scope = _scopeFactory.CreateScope();
            var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
            var cashierCode = await accounting.GetMappedCashAccount(order.PaymentMethod, order.Source);
            
            // ✅ نتحقق من رصيد اليوم الحالي فقط (بناءً على طلب العميل للحسابات المنفصلة يومياً)
            var currentDrawerBalance = await accounting.GetTodayDrawerBalanceAsync(cashierCode);
            if (currentDrawerBalance < refundAmountTotal)
            {
               throw new InvalidOperationException($"عذراً، رصيد الدرج لليوم ({currentDrawerBalance:N2} ج.م) غير كافٍ لإتمام هذا المرتجع الجزئي ({refundAmountTotal:N2} ج.م).");
            }
        }

        var returnedOrderItems = new List<OrderItem>();
        decimal refundAmount = 0;

        foreach (var req in dto.Items)
        {
            var line = order.Items.FirstOrDefault(i => i.Id == req.OrderItemId);
            if (line == null) continue;
            if (req.Quantity <= 0) continue;
            
            int maxCanReturn = line.Quantity - line.ReturnedQuantity;
            if (req.Quantity > maxCanReturn) 
               throw new InvalidOperationException($"Cannot return {req.Quantity} for item {line.ProductNameAr}. Already returned: {line.ReturnedQuantity}. Max remaining: {maxCanReturn}.");

            // 1. Calculate partial values for this item
            var itemTotalReturn = Math.Round((line.UnitPrice * req.Quantity) * discountRatio, 2);
            var itemVatReturn = Math.Round(line.ItemVatAmount * ((decimal)req.Quantity / line.Quantity), 2);
            
            refundAmount += itemTotalReturn;
            line.ReturnedQuantity += req.Quantity; // ✅ Update the item tracking

            // 2. Clone for accounting (to represent the returned part)
            var returnClone = new OrderItem
            {
                ProductId = line.ProductId,
                ProductVariantId = line.ProductVariantId,
                ProductNameAr = line.ProductNameAr,
                Quantity = req.Quantity,
                UnitPrice = line.UnitPrice,
                TotalPrice = itemTotalReturn, // This part is refunded
                ItemVatAmount = itemVatReturn,
                Product = line.Product // For COGS
            };
            returnedOrderItems.Add(returnClone);

            // 3. Update Inventory
            await _inventory.LogMovementAsync(
                InventoryMovementType.ReturnIn,
                req.Quantity, line.ProductId, line.ProductVariantId,
                order.OrderNumber, $"Partial Return: {req.Quantity} units", updatedByUserId
            );
        }

        // 4. Update Order Status
        if (order.Items.All(i => i.Quantity == i.ReturnedQuantity))
        {
           order.Status = OrderStatus.Returned;
           order.PaymentStatus = PaymentStatus.Refunded;
        }
        else if (order.Items.Any(i => i.ReturnedQuantity > 0))
        {
           order.Status = OrderStatus.PartiallyReturned;
        }

        if (!returnedOrderItems.Any()) throw new InvalidOperationException("No items selected for return.");

        // 5. Status History
        order.StatusHistory.Add(new OrderStatusHistory {
            Status = order.Status, // Keep current status (e.g. Delivered)
            Note = $"[مرتجع جزئي] {dto.Reason}: {dto.Note}",
            ChangedByUserId = updatedByUserId,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // 6. Post Accounting
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            try {
                // We need the instances with tracking/includes in the new scope
                var fullOrder = await db.Orders.Include(o => o.Customer).FirstAsync(o => o.Id == orderId);
                // We must pass the returned items data correctly
                await accounting.PostPartialSalesReturnAsync(fullOrder, returnedOrderItems, refundAmount);
            } catch (Exception ex) {
                Console.Error.WriteLine($"[Accounting] Partial return failed: {ex.Message}");
            }
        });

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
