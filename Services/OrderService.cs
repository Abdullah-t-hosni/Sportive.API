using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Utils;
using System.Text;
using System.Text.Json;

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
    private readonly ILogger<OrderService> _logger;
    private readonly SequenceService _seq;

    public OrderService(
        AppDbContext db,
        INotificationService notificationService,
        IEmailService emailService,
        IServiceScopeFactory scopeFactory,
        IInventoryService inventory,
        IConfiguration config,
        ICustomerService customerService,
        IAccountingService accounting,
        ILogger<OrderService> logger,
        SequenceService seq)
    {
        _db = db;
        _notificationService = notificationService;
        _emailService = emailService;
        _scopeFactory = scopeFactory;
        _inventory = inventory;
        _config = config;
        _customerService = customerService;
        _accounting = accounting;
        _logger = logger;
        _seq = seq;
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
                o.CustomerId,
                o.AdminNotes,
                o.CouponCode
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
                .ThenInclude(i => i.Product!)
                    .ThenInclude(p => p.Images)
            .Include(o => o.StatusHistory)
            .Include(o => o.Payments) // ✅ Added
            .FirstOrDefaultAsync(o => o.Id == id);

        if (o == null) return null;

        var customerId = o.CustomerId;
        var customerDto = o.Customer != null 
            ? new CustomerBasicDto(o.Customer.Id, o.Customer.FullName, o.Customer.Email, o.Customer.Phone, o.Customer.FixedDiscount)
            : new CustomerBasicDto(customerId, "Unknown Customer", "", "");

        var salesPersonName = "";
        if (!string.IsNullOrEmpty(o.SalesPersonId))
        {
            var sp = await _db.Users.FirstOrDefaultAsync(u => u.Id == o.SalesPersonId);
            salesPersonName = sp?.FullName ?? "";
        }

        // 💡 SMART FINANCE: Calculate actual paid amount from Journal Entries
        // Use a safe query that handles null accounts or missing lines
        var paidAmountQuery = _db.JournalLines
            .Where(l => l.OrderId == id && l.Credit > 0);
            
        var paidAmount = await paidAmountQuery
            .Where(l => l.Account.Code != null && l.Account.Code.StartsWith("1103"))
            .SumAsync(l => l.Credit);

        return new OrderDetailDto(
            o.Id,
            o.OrderNumber,
            customerDto,
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
                i.Product?.Slug,
                i.Size, i.Color, i.Quantity, i.UnitPrice, i.TotalPrice,
                i.OriginalUnitPrice, i.DiscountAmount,
                i.HasTax, i.VatRateApplied, i.ItemVatAmount, i.ReturnedQuantity
            )).ToList(),
            o.StatusHistory.OrderByDescending(h => h.CreatedAt).Select(h => new OrderStatusHistoryDto(
                h.Status.ToString(), h.Note, h.CreatedAt
            )).ToList(),
            o.Payments.Select(p => new OrderDetailPaymentDto( // ✅ Populate Payments
                p.Method.ToString(),
                p.Amount,
                p.Reference,
                p.Notes,
                p.CreatedAt
            )).ToList(),
            salesPersonName,
            null, 
            0, 
            Math.Max(o.PaidAmount, paidAmount), // ✅ Show the real paid amount (max of stored or ledger)
            o.Source.ToString(),
            o.AttachmentUrl, 
            o.AttachmentPublicId,
            o.CouponCode
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

        Order? order = null;

        var strategy = _db.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var now = TimeHelper.GetEgyptTime();
                // 1. Handle Customer
                if (!customerId.HasValue)
                {
                    var phone = string.IsNullOrEmpty(dto.CustomerPhone) ? "0000000000" : dto.CustomerPhone;
                    var existing = await _db.Customers.FirstOrDefaultAsync(c => c.Phone == phone);

                    if (existing != null)
                    {
                        customerId = existing.Id;
                        // ✅ ضمان وجود حساب محاسبي حتى للعملاء القدامى
                        await _customerService.EnsureCustomerAccountAsync(customerId.Value);
                    }
                    else
                    {
                        var c = new Customer
                        {
                            FullName = dto.CustomerName ?? "Walk-in Customer",
                            Phone = phone,
                            Email = $"{phone}@pos.sportive.com",
                            CreatedAt = TimeHelper.GetEgyptTime(),
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

                // 🔎 CREDIT POLICY: No credit for anonymous customers
                bool isCreditOrder = dto.PaymentMethod == PaymentMethod.Credit || (dto.Payments != null && dto.Payments.Any(p => p.Method == PaymentMethod.Credit));
                if (isCreditOrder)
                {
                    var customer = await _db.Customers.FindAsync(customerId.Value);
                    if (customer == null || 
                        customer.FullName == "Walk-in Customer" || 
                        customer.Phone == "0000000000" || 
                        string.IsNullOrEmpty(customer.Phone))
                    {
                        throw new ArgumentException("عفواً، لا يمكن البيع الآجل لعميل نقدي مجهول. يرجى اختيار أو تسجيل عميل باسم ورقم هاتف أولاً لإثبات المديونية.");
                    }
                }

                var actualSource = (int)dto.Source == 0 ? OrderSource.Website : dto.Source;
                order = new Order
                {
                    CustomerId = customerId.Value,
                    OrderNumber = await GenerateOrderNumberAsync(actualSource),
                    Status = actualSource == OrderSource.POS ? OrderStatus.Delivered : OrderStatus.Pending,
                    FulfillmentType = dto.FulfillmentType,
                    PaymentMethod = dto.PaymentMethod,
                    PaymentStatus = (actualSource == OrderSource.POS && (dto.PaymentMethod != PaymentMethod.Credit && dto.PaymentMethod != (PaymentMethod)7)) ? PaymentStatus.Paid : PaymentStatus.Pending,
                    DeliveryAddressId = (dto.DeliveryAddressId.HasValue && dto.DeliveryAddressId.Value > 0) 
                                        ? (await _db.Addresses.AnyAsync(a => a.Id == dto.DeliveryAddressId.Value && a.CustomerId == customerId.Value) ? dto.DeliveryAddressId : null) 
                                        : null,
                    PickupScheduledAt = dto.PickupScheduledAt,
                    CustomerNotes = dto.CustomerNotes,
                    CouponCode = dto.CouponCode,
                    SalesPersonId = dto.SalesPersonId,
                    Source = actualSource,
                    AdminNotes = dto.Note,
                    DiscountAmount = 0,
                    AttachmentUrl = dto.AttachmentUrl,
                    AttachmentPublicId = dto.AttachmentPublicId,
                    CreatedAt = now
                };

                var activeDiscounts = await _db.ProductDiscounts
                    .Where(d => d.IsActive && d.ValidFrom <= now && d.ValidTo >= now)
                    .ToListAsync();

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

                        decimal originalUnitPrice = product.Price;
                        if (variant?.PriceAdjustment.HasValue == true)
                            originalUnitPrice += variant.PriceAdjustment.Value;

                        decimal unitPrice;
                        if (actualSource == OrderSource.POS)
                        {
                            unitPrice = item.UnitPrice;
                        }
                        else
                        {
                            var disc = activeDiscounts.FirstOrDefault(d => d.ProductId == product.Id)
                                    ?? activeDiscounts.FirstOrDefault(d => d.CategoryId == product.CategoryId)
                                    ?? activeDiscounts.FirstOrDefault(d => d.BrandId == product.BrandId);

                            if (disc != null && item.Quantity >= disc.MinQty)
                            {
                                unitPrice = disc.DiscountType == DiscountType.Percentage 
                                    ? Math.Round(originalUnitPrice - (originalUnitPrice * disc.DiscountValue / 100), 2)
                                    : Math.Round(originalUnitPrice - disc.DiscountValue, 2);
                            }
                            else
                            {
                                unitPrice = product.DiscountPrice ?? originalUnitPrice;
                            }
                        }
                        
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
                            OriginalUnitPrice = originalUnitPrice,
                            DiscountAmount = (originalUnitPrice - unitPrice) * item.Quantity,
                            TotalPrice = (actualSource == OrderSource.POS) ? item.TotalPrice : (unitPrice * item.Quantity),
                            HasTax = item.HasTax ?? product.HasTax,
                            VatRateApplied = item.VatRate ?? product.VatRate ?? (store?.VatRatePercent ?? 14),
                            CreatedAt = TimeHelper.GetEgyptTime()
                        };

                        if (orderItem.HasTax)
                        {
                            var rate = (orderItem.VatRateApplied ?? 14) / 100m;
                            decimal net = Math.Round(orderItem.TotalPrice / (1 + rate), 2);
                            orderItem.ItemVatAmount = orderItem.TotalPrice - net;
                            order.TotalVatAmount += orderItem.ItemVatAmount;
                        }

                        var availableStock = variant?.StockQuantity ?? (product.TotalStock);
                        if (store != null && !store.AllowBackorders && item.Quantity > availableStock)
                        {
                            throw new ArgumentException(
                                actualSource == OrderSource.POS
                                ? $"الكمية المطلوبة ({item.Quantity}) من {product.NameAr} غير متاحة في المخزون (المتاح: {availableStock})"
                                : $"Requested quantity for {product.NameAr} is not available in stock.");
                        }

                        order.Items.Add(orderItem);
                        order.SubTotal += (orderItem.OriginalUnitPrice * orderItem.Quantity);
                        order.DiscountAmount += orderItem.DiscountAmount;

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
                    var cartItems = await _db.CartItems.Include(c => c.Product).ThenInclude(p => p!.Variants)
                        .Include(c => c.ProductVariant)
                        .Where(c => c.CustomerId == customerId.Value).ToListAsync();
                    
                    if (!cartItems.Any()) throw new ArgumentException("Cart is empty.");

                    foreach (var ci in cartItems)
                    {
                    if (ci.Product == null) continue;
                    var availableStock = ci.ProductVariant?.StockQuantity ?? ci.Product.TotalStock;
                    if (store != null && !store.AllowBackorders && ci.Quantity > availableStock)
                    {
                        throw new ArgumentException($"الكمية المطلوبة ({ci.Quantity}) من {ci.Product.NameAr} غير متاحة في المخزون حالياً.");
                    }

                        decimal originalUnitPrice = ci.Product.Price;
                        if (ci.ProductVariant?.PriceAdjustment.HasValue == true)
                            originalUnitPrice += ci.ProductVariant.PriceAdjustment.Value;

                        var disc = activeDiscounts.FirstOrDefault(d => d.ProductId == ci.ProductId)
                                ?? activeDiscounts.FirstOrDefault(d => d.CategoryId == ci.Product.CategoryId)
                                ?? activeDiscounts.FirstOrDefault(d => d.BrandId == ci.Product.BrandId);

                        decimal unitPrice;
                        if (disc != null && ci.Quantity >= disc.MinQty)
                        {
                            unitPrice = disc.DiscountType == DiscountType.Percentage 
                                ? Math.Round(originalUnitPrice - (originalUnitPrice * disc.DiscountValue / 100), 2)
                                : Math.Round(originalUnitPrice - disc.DiscountValue, 2);
                        }
                        else
                        {
                            unitPrice = ci.Product.DiscountPrice ?? originalUnitPrice;
                        }
                        
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
                            OriginalUnitPrice = originalUnitPrice,
                            DiscountAmount = (originalUnitPrice - unitPrice) * ci.Quantity,
                            TotalPrice = unitPrice * ci.Quantity,
                            HasTax = ci.Product.HasTax,
                            VatRateApplied = ci.Product.VatRate ?? (store?.VatRatePercent ?? 14),
                            CreatedAt = TimeHelper.GetEgyptTime()
                        };

                        if (orderItem.HasTax)
                        {
                            var rate = (orderItem.VatRateApplied ?? 14) / 100m;
                            decimal net = Math.Round(orderItem.TotalPrice / (1 + rate), 2);
                            orderItem.ItemVatAmount = orderItem.TotalPrice - net;
                            order.TotalVatAmount += orderItem.ItemVatAmount;
                        }

                        order.Items.Add(orderItem);
                        order.SubTotal += (orderItem.OriginalUnitPrice * orderItem.Quantity);
                        order.DiscountAmount += orderItem.DiscountAmount;

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

                // ✅ FIX: مسح السلة دايماً بعد أي طلب Website — سواء جه من dto.Items أو من السلة مباشرة
                if (order.Source == OrderSource.Website && customerId.HasValue)
                {
                    var remainingCart = await _db.CartItems
                        .Where(c => c.CustomerId == customerId.Value)
                        .ToListAsync();
                    if (remainingCart.Any())
                        _db.CartItems.RemoveRange(remainingCart);
                }

                if (order.Source == OrderSource.Website && order.FulfillmentType == FulfillmentType.Delivery)
                {
                    decimal fee = store?.FixedDeliveryFee ?? 50;
                    decimal? threshold = store?.FreeDeliveryAt ?? 2000;

                    if (dto.DeliveryAddressId.HasValue)
                    {
                        var addr = await _db.Addresses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == dto.DeliveryAddressId.Value);
                        if (addr != null && !string.IsNullOrEmpty(addr.City))
                        {
                            var city = addr.City.Trim().ToLower();
                            var zone = await _db.ShippingZones.AsNoTracking()
                                .Where(z => z.IsActive)
                                .ToListAsync();
                            
                            var matched = zone.FirstOrDefault(z => z.Governorates.ToLower().Split(',').Any(g => g.Trim() == city));
                            if (matched != null)
                            {
                                fee = matched.Fee;
                                threshold = matched.FreeThreshold;
                            }
                        }
                    }

                    order.DeliveryFee = (threshold.HasValue && order.SubTotal >= threshold.Value) ? 0 : fee;
                }

                order.DiscountAmount += (dto.DiscountAmount ?? 0);
                order.TotalAmount = order.SubTotal + order.DeliveryFee - order.DiscountAmount;

                // 💡 Initial Paid Amount calculation for POS Mixed payments & Structured Payment Table
                if (order.Source == OrderSource.POS)
                {
                    if (dto.Payments != null && dto.Payments.Any())
                    {
                        // ✅ PRO WAY: Use structured payments from DTO
                        decimal totalPaid = 0;
                        foreach (var p in dto.Payments)
                        {
                            if (p.Amount <= 0 || p.Method == PaymentMethod.Credit) continue;
                            
                            totalPaid += p.Amount;
                            order.Payments.Add(new OrderPayment 
                            { 
                                Method = p.Method, 
                                Amount = p.Amount, 
                                CreatedAt = now 
                            });
                        }
                        
                        // 💎 STRICT VALUATION: PaidAmount must ONLY be the sum of REAL (Non-Credit) payments
                        order.PaidAmount = totalPaid;
                        
                        // Set status based on total paid vs total amount
                        if (order.PaidAmount >= order.TotalAmount - 0.01m) 
                            order.PaymentStatus = PaymentStatus.Paid;
                        else
                            order.PaymentStatus = PaymentStatus.Pending;
                    }
                    else if (order.PaymentMethod != PaymentMethod.Credit && order.PaymentMethod != (PaymentMethod)7)
                    {
                        order.PaidAmount = order.TotalAmount;
                        // Single payment method - record it
                        order.Payments.Add(new OrderPayment 
                        { 
                            Method = order.PaymentMethod, 
                            Amount = order.TotalAmount, 
                            CreatedAt = now 
                        });
                    }
                    else if (order.PaymentMethod == (PaymentMethod)7 && !string.IsNullOrEmpty(dto.Note))
                    {
                        // 🔄 FALLBACK: Parse JSON from Note (Legacy Support)
                        try 
                        {
                            using var doc = JsonDocument.Parse(dto.Note);
                            var root = doc.RootElement;
                            JsonElement mixedProps;
                            if (root.TryGetProperty("mixed", out mixedProps)) { } 
                            else if (root.TryGetProperty("amounts", out mixedProps)) { }
                            else { mixedProps = root; }
                            
                            decimal totalPaid = 0;
                            foreach (var prop in mixedProps.EnumerateObject()) 
                            {
                                var pmName = prop.Name.ToLower();
                                if (new[] { "credit", "remaining", "deferred", "debt", "change", "date" }.Contains(pmName)) continue;
                                
                                if (decimal.TryParse(prop.Value.ToString(), out decimal val) && val > 0) 
                                {
                                    totalPaid += val;
                                    var method = pmName switch {
                                        "cash" => PaymentMethod.Cash,
                                        "bank" => PaymentMethod.Bank,
                                        "visa" or "creditcard" or "fawry" => PaymentMethod.CreditCard,
                                        "vodafone" => PaymentMethod.Vodafone,
                                        "instapay" => PaymentMethod.InstaPay,
                                        _ => (PaymentMethod?)null
                                    };
                                    
                                    if (method.HasValue)
                                    {
                                        order.Payments.Add(new OrderPayment 
                                        { 
                                            Method = method.Value, 
                                            Amount = val, 
                                            CreatedAt = now 
                                        });
                                    }
                                }
                            }
                            order.PaidAmount = totalPaid;
                            if (order.PaidAmount >= order.TotalAmount) order.PaymentStatus = PaymentStatus.Paid;
                        } 
                        catch (Exception ex) 
                        { 
                            _logger.LogWarning(ex, "Failed to parse mixed payment JSON during order creation for {OrderNum}", order.OrderNumber);
                        }
                    }
                }
                else if (order.Source == OrderSource.Website && (order.PaymentMethod == PaymentMethod.Vodafone || order.PaymentMethod == PaymentMethod.InstaPay))
                {
                    // For website digital payments that are "Pending" initially but will be "Paid" on confirmation
                    // We can track them here or when confirmed.
                }

                // 4. Validate Business Rules/Settings
                if (order.Source == OrderSource.Website && store != null && order.TotalAmount < store.MinOrderAmount)
                {
                    throw new ArgumentException($"الحد الأدنى للطلب هو {store.MinOrderAmount:N2} ج.م. المبلغ الحالي: {order.TotalAmount:N2} ج.م");
                }

                if (store != null && !store.AllowBackorders)
                {
                     // Stock specifically already checked for each item, but we double-check here if it was a website order with no stock
                }
                _db.Orders.Add(order);
                order.StatusHistory.Add(new OrderStatusHistory { Status = order.Status, CreatedAt = TimeHelper.GetEgyptTime(), Note = "Order Created." });

                // ✅ UPDATE COUPON USAGE
                if (!string.IsNullOrEmpty(order.CouponCode))
                {
                    var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == order.CouponCode.ToUpper());
                    if (coupon != null)
                    {
                        if (coupon.MaxUsageCount.HasValue && coupon.CurrentUsageCount >= coupon.MaxUsageCount)
                            throw new ArgumentException("هذا الكوبون تم استخدامه بالكامل بالحد الأقصى");

                        coupon.CurrentUsageCount++;
                    }
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return (await GetOrderByIdAsync(order.Id))!;
            }
            catch { await tx.RollbackAsync(); throw; }
        });

        _ = PostSalesOrderWithRetryAsync(order!.Id, order.OrderNumber);

        // 5. Notifications & Email
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (order == null) return;
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
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Notifications] Admin email for order {OrderNumber} failed", order.OrderNumber);
            }
        });

        return result;
    }

    public async Task<OrderDetailDto> UpdateOrderStatusAsync(int orderId, UpdateOrderStatusDto dto, string updatedByUserId)
    {
        var order = await _db.Orders.Include(o => o.StatusHistory).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw new KeyNotFoundException("Order not found.");

        var oldStatus = order.Status;
        order.Status = dto.Status;
        order.UpdatedAt = TimeHelper.GetEgyptTime();
        order.StatusHistory.Add(new OrderStatusHistory {
            Status = dto.Status, Note = dto.Note, ChangedByUserId = updatedByUserId, CreatedAt = TimeHelper.GetEgyptTime()
        });

        // 💡 AUTO-FLOW: For Website orders, Digital Payments (Vodafone, InstaPay, Visa) are paid upon Confirmation. Cash is paid ONLY upon Delivery.
        if (order.Source == OrderSource.Website && dto.Status == OrderStatus.Confirmed)
        {
            if (order.PaymentMethod == PaymentMethod.Vodafone || 
                order.PaymentMethod == PaymentMethod.InstaPay || 
                order.PaymentMethod == PaymentMethod.CreditCard)
            {
                order.PaymentStatus = PaymentStatus.Paid;
                order.PaidAmount = order.TotalAmount;
            }
        }

        if (dto.Status == OrderStatus.Delivered)
        {
            order.ActualDeliveryDate = TimeHelper.GetEgyptTime();
            if (order.PaymentMethod != PaymentMethod.Credit)
            {
                order.PaymentStatus = PaymentStatus.Paid;
                order.PaidAmount = order.TotalAmount; // ✅ تحديث المبلغ المدفوع عند التسليم الفعلي (للكاش والوسائل الأخرى)
            }

            // Notification on Delivery
            _ = Task.Run(async () => {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                var cust = await db.Customers.FindAsync(order.CustomerId);
                if (cust != null && !string.IsNullOrEmpty(cust.AppUserId)) {
                   await notificationService.SendAsync(cust.AppUserId, 
                      "تهانينا! تم توصيل طلبك", "Order Delivered!",
                      $"تم توصيل طلبك رقم {order.OrderNumber} بنجاح. يسعدنا تقييم تجربتك!", 
                      $"Your order #{order.OrderNumber} has been delivered. We'd love to hear your feedback!",
                      "Order", order.Id);
                }
            });
        }

        if (dto.Status == OrderStatus.Returned && order.Source == OrderSource.POS)
        {
            // Mark all items as returned (inventory handled below)
            var orderWithItems = await _db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == orderId);
            foreach (var it in orderWithItems.Items) it.ReturnedQuantity = it.Quantity;
        }

        // ✅ REVERT: If reverting FROM Returned/Cancelled → reverse accounting & inventory
        if ((oldStatus == OrderStatus.Returned || oldStatus == OrderStatus.Cancelled) &&
             dto.Status != OrderStatus.Returned && dto.Status != OrderStatus.Cancelled)
        {
            // 1️⃣ Remove SalesReturn journal entries for this order
            var returnEntries = await _db.JournalEntries
                .Include(e => e.Lines)
                .Where(e => e.Type == JournalEntryType.SalesReturn && e.Reference != null && e.Reference.StartsWith(order.OrderNumber))
                .ToListAsync();

            if (returnEntries.Any())
            {
                _db.JournalLines.RemoveRange(returnEntries.SelectMany(e => e.Lines));
                _db.JournalEntries.RemoveRange(returnEntries);
                _logger.LogInformation("[Accounting] Voided {Count} SalesReturn entries for order {Ref} on status revert.", returnEntries.Count, order.OrderNumber);
            }

            // 2️⃣ Reverse the inventory ReturnIn movements (re-deduct stock)
            if (oldStatus == OrderStatus.Returned)
            {
                var orderWithItems = await _db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == orderId);
                foreach (var item in orderWithItems.Items)
                {
                    if ((item.ProductId ?? 0) > 0)
                    {
                        await _inventory.LogMovementAsync(
                            InventoryMovementType.Sale,
                            -item.Quantity, item.ProductId, item.ProductVariantId,
                            order.OrderNumber, "Revert: Order status changed from Returned", updatedByUserId
                        );
                    }
                }
                // Reset returned quantities on items
                foreach (var it in orderWithItems.Items) it.ReturnedQuantity = 0;
            }
        }

        // Post accounting if paid (Website orders)
        if ((dto.Status == OrderStatus.Delivered || dto.Status == OrderStatus.Confirmed) &&
            order.Source == OrderSource.Website && order.PaymentStatus == PaymentStatus.Paid)
        {
            _ = PostOrderPaymentWithRetryAsync(orderId);
        }

        // Inventory movement + Coupon restore when moving INTO Returned/Cancelled
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

            // ✅ RESTORE COUPON USAGE IF CANCELLED OR FULLY RETURNED
            if (!string.IsNullOrEmpty(order.CouponCode))
            {
                var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == order.CouponCode.ToUpper());
                if (coupon != null && coupon.CurrentUsageCount > 0)
                {
                    coupon.CurrentUsageCount--;
                }
            }
        }

        await _db.SaveChangesAsync();

        if (dto.Status == OrderStatus.Returned || dto.Status == OrderStatus.Cancelled)
        {
            _ = PostSalesReturnWithRetryAsync(orderId);
        }

        return (await GetOrderByIdAsync(orderId))!;
    }

    public async Task<OrderDetailDto> ProcessPartialReturnAsync(int orderId, PartialReturnDto dto, string updatedByUserId)
    {
        var returnedOrderItems = new List<OrderItem>();
        decimal refundAmount = 0;

        var strategy = _db.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
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

                // NOTE: Drawer balance check is enforced by the frontend using real order cash data.
                // Backend validates item quantities and business rules only.

                returnedOrderItems = new List<OrderItem>();
                refundAmount = 0;

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

                   // ✅ RESTORE COUPON USAGE IF FULLY RETURNED
                   if (!string.IsNullOrEmpty(order.CouponCode))
                   {
                       var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == order.CouponCode.ToUpper());
                       if (coupon != null && coupon.CurrentUsageCount > 0)
                       {
                           coupon.CurrentUsageCount--;
                       }
                   }
                }
                else if (order.Items.Any(i => i.ReturnedQuantity > 0))
                {
                   order.Status = OrderStatus.PartiallyReturned;
                }

                if (!returnedOrderItems.Any()) throw new InvalidOperationException("No items selected for return.");

                // 5. Status History
                order.StatusHistory.Add(new OrderStatusHistory {
                    Status = order.Status,
                    Note = $"[مرتجع جزئي] {dto.Reason}: {dto.Note}",
                    ChangedByUserId = updatedByUserId,
                    CreatedAt = TimeHelper.GetEgyptTime()
                });

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return (await GetOrderByIdAsync(orderId))!;
            }
            catch { await tx.RollbackAsync(); throw; }
        });

        // 6. Post Accounting
        _ = PostPartialReturnWithRetryAsync(orderId, returnedOrderItems, refundAmount, dto.RefundAccountId);

        return result;
    }

    public async Task<string> GenerateOrderNumberAsync(OrderSource source = OrderSource.Website)
    {
        var store = await _db.StoreInfo.AsNoTracking().FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        var basePrefix = store?.OrderNumberPrefix ?? "SPT";

        var prefix = source == OrderSource.POS ? "POS" : basePrefix;
        return await _seq.NextAsync(prefix, async (db, pattern) =>
        {
            var max = await db.Orders
                .Where(o => EF.Functions.Like(o.OrderNumber, pattern))
                .Select(o => o.OrderNumber)
                .ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                      .DefaultIfEmpty(0).Max();
        });
    }

    public async Task SyncAllOrderAccountingAsync()
    {
        var orders = await _db.Orders.Include(o => o.Customer).Include(o => o.Items).ThenInclude(i => i.Product)
            .Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Pending).ToListAsync();

        var orderNums = orders.Select(o => o.OrderNumber.Trim().ToLower()).ToList();
        var entries = await _db.JournalEntries.Include(j => j.Lines)
            .Where(j => j.Type == JournalEntryType.SalesInvoice && j.Reference != null && orderNums.Contains(j.Reference))
            .ToListAsync();

        var entryMap = entries.GroupBy(e => e.Reference!.Trim().ToLower()).ToDictionary(g => g.Key, g => g.First());

        foreach (var order in orders)
        {
            try {
                var orderNum = order.OrderNumber.Trim().ToLower();
                entryMap.TryGetValue(orderNum, out var entry);

                if (entry == null || !entry.Lines.Any() || entry.Lines.All(l => l.CustomerId == null))
                {
                    if (entry != null) { 
                        _db.JournalLines.RemoveRange(entry.Lines); 
                        _db.JournalEntries.Remove(entry); 
                        await _db.SaveChangesAsync(); 
                    }
                    await _accounting.PostSalesOrderAsync(order);
                }
                if (order.PaymentStatus == PaymentStatus.Paid) await _accounting.PostOrderPaymentAsync(order);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Accounting] SyncAllOrderAccounting failed for order {OrderNumber}", order.OrderNumber);
            }
        }
    }

    // ── Retry helpers ─────────────────────────────────────

    private async Task PostSalesOrderWithRetryAsync(int orderId, string orderNumber)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var order = await db.Orders
                    .Include(o => o.Customer)
                    .Include(o => o.Payments)
                    .Include(o => o.Items).ThenInclude(i => i.Product)
                    .Include(o => o.DeliveryAddress)
                    .FirstAsync(o => o.Id == orderId);
                await accounting.PostSalesOrderAsync(order);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "[Accounting] PostSalesOrder attempt {Attempt}/{Max} failed for {Number}. Retrying...", attempt, maxAttempts, orderNumber);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Accounting] PostSalesOrder permanently failed for {Number} after {Max} attempts.", orderNumber, maxAttempts);
                
                // ✅ ALERT ADMIN: Send a system notification so they can fix the mapping
                using var scope = _scopeFactory.CreateScope();
                var notify = scope.ServiceProvider.GetRequiredService<INotificationService>();
                await notify.SendAsync(
                    null,
                    "فشل إنشاء قيد محاسبي",
                    "Accounting Post Failed",
                    $"حدث خطأ أثناء ترحيل الطلب {orderNumber} محاسبياً. يرجى مراجعة صفحة الربط المالي. السبب: {ex.Message}",
                    $"Error posting journal entry for order {orderNumber}. Reason: {ex.Message}",
                    "Alert"
                );
            }
        }
    }

    private async Task PostOrderPaymentWithRetryAsync(int orderId)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var order = await db.Orders
                    .Include(o => o.Customer)
                    .Include(o => o.Payments)
                    .FirstAsync(o => o.Id == orderId);
                await accounting.PostOrderPaymentAsync(order);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "[Accounting] PostOrderPayment attempt {Attempt}/{Max} failed for order {OrderId}. Retrying...", attempt, maxAttempts, orderId);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Accounting] PostOrderPayment permanently failed for order {OrderId} after {Max} attempts.", orderId, maxAttempts);

                // ✅ ALERT ADMIN: Fixed logic to get notification service correctly
                using var scope = _scopeFactory.CreateScope();
                var notify = scope.ServiceProvider.GetRequiredService<INotificationService>();
                await notify.SendAsync(
                    null,
                    "فشل إنشاء قيد تحصيل",
                    "Payment Post Failed",
                    $"فشل نظام التحصيل التلقائي للطلب (ID:{orderId}). يرجى مراجعة الربط المالي. السبب: {ex.Message}",
                    $"Automated payment collection failed for order ID:{orderId}. Reason: {ex.Message}",
                    "Alert"
                );
            }
        }
    }

    private async Task PostSalesReturnWithRetryAsync(int orderId)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var order = await db.Orders
                    .Include(o => o.Customer)
                    .Include(o => o.Payments)
                    .Include(o => o.Items).ThenInclude(i => i.Product)
                    .Include(o => o.DeliveryAddress)
                    .FirstAsync(o => o.Id == orderId);
                await accounting.PostSalesReturnAsync(order);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "[Accounting] PostSalesReturn attempt {Attempt}/{Max} failed for order {OrderId}. Retrying...", attempt, maxAttempts, orderId);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Accounting] PostSalesReturn permanently failed for order {OrderId} after {Max} attempts.", orderId, maxAttempts);
            }
        }
    }

    private async Task PostPartialReturnWithRetryAsync(int orderId, List<OrderItem> returnedItems, decimal refundAmount, int? refundAccountId = null)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var order = await db.Orders
                    .Include(o => o.Customer)
                    .Include(o => o.Payments)
                    .FirstAsync(o => o.Id == orderId);
                await accounting.PostPartialSalesReturnAsync(order, returnedItems, refundAmount, refundAccountId);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "[Accounting] PostPartialReturn attempt {Attempt}/{Max} failed for order {OrderId}. Retrying...", attempt, maxAttempts, orderId);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Accounting] PostPartialReturn permanently failed for order {OrderId} after {Max} attempts.", orderId, maxAttempts);
            }
        }
    }
}
