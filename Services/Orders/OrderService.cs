using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Utils;
using System.Text;
using System.Text.Json;
using Hangfire;

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
    private readonly ITranslator _t;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly IDashboardEventService _dashboardEvents;
    private readonly EncryptionHelper _encryptionHelper;
    private readonly ITaxIntegrationService _taxIntegrationService;

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
        SequenceService seq,
        ITranslator t,
        IBackgroundJobClient backgroundJobs,
        IDashboardEventService dashboardEvents,
        EncryptionHelper encryptionHelper,
        ITaxIntegrationService taxIntegrationService)
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
        _t = t;
        _backgroundJobs = backgroundJobs;
        _dashboardEvents = dashboardEvents;
        _encryptionHelper = encryptionHelper;
        _taxIntegrationService = taxIntegrationService;
    }

    public async Task<PaginatedResult<OrderSummaryDto>> GetOrdersAsync(
        int page, int pageSize, OrderStatus? status = null, string? search = null,
        int? customerId = null, DateTime? fromDate = null, DateTime? toDate = null, string? salesPersonId = null, OrderSource? source = null, PaymentMethod? paymentMethod = null,
        string? orderBy = null, bool descending = false, int? branchId = null, int? warehouseId = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 2000);
        var query = _db.Orders
            .Include(o => o.Customer)
            .AsNoTracking();

        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (source.HasValue) query = query.Where(o => o.Source == source.Value);
        if (paymentMethod.HasValue) query = query.Where(o => o.PaymentMethod == paymentMethod.Value);
        if (customerId.HasValue) query = query.Where(o => o.CustomerId == customerId.Value);
        // 🕒 BUSINESS DAY OFFSET: The day ends at 2 AM.
        if (fromDate.HasValue) query = query.Where(o => o.CreatedAt >= fromDate.Value.Date.AddHours(TimeHelper.GetBusinessDayEndHour()));
        if (toDate.HasValue) 
        {
            var endOfBusinessDay = toDate.Value.Date.AddDays(1).AddHours(TimeHelper.GetBusinessDayEndHour()).AddTicks(-1);
            query = query.Where(o => o.CreatedAt <= endOfBusinessDay);
        }
        if (!string.IsNullOrEmpty(salesPersonId)) query = query.Where(o => o.SalesPersonId == salesPersonId);
        if (branchId.HasValue) query = query.Where(o => o.BranchId == branchId.Value);
        if (warehouseId.HasValue) query = query.Where(o => o.WarehouseId == warehouseId.Value);

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(o => o.OrderNumber.Contains(search) || 
                                     o.Customer.FullName.Contains(search));
        }

        var total = await query.CountAsync();

        if (!string.IsNullOrEmpty(orderBy) && orderBy.Equals("updatedAt", StringComparison.OrdinalIgnoreCase))
        {
            query = descending 
                ? query.OrderByDescending(o => o.UpdatedAt ?? o.CreatedAt)
                : query.OrderBy(o => o.UpdatedAt ?? o.CreatedAt);
        }
        else
        {
            query = query.OrderByDescending(o => o.CreatedAt);
        }

        var items = await query
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
                o.PaidAmount,
                o.CreatedAt,
                o.Items.Count,
                o.Source.ToString(),
                o.PaymentMethod.ToString(),
                o.PaymentStatus.ToString(),
                o.CustomerId,
                o.AdminNotes,
                o.CouponCode,
                o.Payments.Select(p => new OrderDetailPaymentDto(p.Method.ToString(), p.Amount, null, null, p.CreatedAt)).ToList(),
                o.Items.Sum(i => (decimal?)i.ReturnedQuantity * i.UnitPrice) ?? 0,
                o.SalesPersonId,
                o.DiscountAmount,
                o.TemporalDiscount,
                o.UpdatedAt,
                o.TaxAuthorityQrCode
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
            : new CustomerBasicDto(customerId, _t.Get("Orders.UnknownCustomer"), "", "");

        var salesPersonName = "";
        if (!string.IsNullOrEmpty(o.SalesPersonId))
        {
            if (int.TryParse(o.SalesPersonId, out var empId))
            {
                var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == empId);
                salesPersonName = emp?.Name ?? "";
            }
            
            if (string.IsNullOrEmpty(salesPersonName))
            {
                var sp = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == o.SalesPersonId);
                salesPersonName = sp?.FullName ?? "";
            }
        }

        // 💡 SMART FINANCE: Calculate actual paid amount from Journal Entries
        // Use a safe query that handles null accounts or missing lines
        var paidAmountQuery = _db.JournalLines
            .Where(l => l.OrderId == id && l.Credit > 0 && l.JournalEntry.Type != JournalEntryType.SalesReturn);
            
        var paidAmount = await paidAmountQuery
            .Where(l => l.Account.Code != null && l.Account.Code.StartsWith("1107"))
            .SumAsync(l => l.Credit);

        var itemDtos = o.Items.Select(i => new OrderItemDto(
            i.Id, i.ProductId, i.ProductVariantId, i.ProductNameAr, i.ProductNameEn, i.SKU, i.Product?.Images?.FirstOrDefault(img => img.IsMain)?.ImageUrl ?? "",
            i.Product?.Slug,
            i.Size, i.Color, i.Quantity, i.UnitPrice, i.TotalPrice,
            i.OriginalUnitPrice, i.DiscountAmount,
            i.HasTax, i.VatRateApplied, i.ItemVatAmount, i.ReturnedQuantity
        )).ToList();

        // 💡 FETCH HISTORY WITH NAMES
        var historyDtos = new List<OrderStatusHistoryDto>();
        var allUsers = await _db.Users.AsNoTracking().Select(u => new { u.Id, u.FullName }).ToListAsync();
        var allEmps = await _db.Employees.AsNoTracking().Select(e => new { e.Id, e.Name }).ToListAsync();

        foreach (var h in o.StatusHistory.OrderByDescending(h => h.CreatedAt))
        {
            string? name = null;
            if (!string.IsNullOrEmpty(h.ChangedByUserId))
            {
                name = allUsers.FirstOrDefault(u => u.Id == h.ChangedByUserId)?.FullName;
                if (string.IsNullOrEmpty(name) && int.TryParse(h.ChangedByUserId, out var eid))
                {
                    name = allEmps.FirstOrDefault(e => e.Id == eid)?.Name;
                }
            }
            if (string.IsNullOrEmpty(name) && h.Note != null && (h.Note.Contains(_t.Get("Orders.StatusCreated")) || h.Note.Contains("Order Created")))
            {
                name = salesPersonName;
            }
            historyDtos.Add(new OrderStatusHistoryDto(h.Status.ToString(), h.Note, h.CreatedAt, name));
        }

        // ✅ Populate Payments from Order.Payments
        var payments = o.Payments.Select(p => new OrderDetailPaymentDto(
            p.Method.ToString(),
            p.Amount,
            p.Reference,
            p.Notes,
            p.CreatedAt
        )).ToList();

        // 💡 SMART FINANCE: Also include payments made via Receipt Vouchers for this order
        var vouchers = await _db.ReceiptVouchers
            .Where(v => v.OrderId == id && v.JournalEntryId != null)
            .Select(v => new OrderDetailPaymentDto(
                v.PaymentMethod.ToString(),
                v.Amount,
                v.VoucherNumber,
                v.Description,
                v.VoucherDate
            ))
            .ToListAsync();

        payments.AddRange(vouchers);

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
            o.TemporalDiscount,
            o.DeliveryFee,
            o.TotalAmount,
            o.CustomerNotes,
            o.AdminNotes,
            o.CreatedAt,
            itemDtos,
            historyDtos,
            payments, 
            salesPersonName,
            null, 
            0, 
            Math.Max(o.PaidAmount, paidAmount), 
            o.Source.ToString(),
            o.AttachmentUrl, 
            o.AttachmentPublicId,
            o.CouponCode,
            GenerateOrderHash(o.OrderNumber),
            o.TaxAuthorityQrCode
        );
    }

    private static string GenerateOrderHash(string orderNumber)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes("SportiveSecretInvoiceSaltKey2026"));
        var hashBytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"invoice-{orderNumber}"));
        return Convert.ToHexString(hashBytes).ToLower().Substring(0, 10);
    }

    public async Task<PaginatedResult<OrderSummaryDto>> GetCustomerOrdersAsync(int customerId, int page, int pageSize)
    {
        return await GetOrdersAsync(page, pageSize, customerId: customerId, paymentMethod: null);
    }

    public async Task<OrderDetailDto> CreateOrderAsync(int? customerId, CreateOrderDto dto)
    {
        using var activity = Sportive.API.Utils.Telemetry.ActivitySource.StartActivity("Create Order");
        if (activity != null)
        {
            activity.SetTag("order.source", dto.Source.ToString());
            activity.SetTag("customer.id", customerId?.ToString() ?? "Guest");
        }

        if (customerId.HasValue && (customerId.Value <= 0 || !await _db.Customers.AnyAsync(c => c.Id == customerId.Value)))
        {
            customerId = null;
        }

        var store = await _db.StoreInfo.FirstOrDefaultAsync(s => s.StoreConfigId == 1);

        Order? order = null;

        var strategy = _db.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                var now = TimeHelper.GetEgyptTime();
                // 1. Handle Customer
                if (!customerId.HasValue)
                {
                    var phone = string.IsNullOrEmpty(dto.CustomerPhone) ? "0000000000" : dto.CustomerPhone;
                    
                    // 🛡️ RACE CONDITION PROTECTION:
                    // If multiple requests for the same new phone number come in simultaneously, 
                    // Serializable transaction helps, but we also catch DbUpdateException for safety.
                    try
                    {
                        var generatedEmail = $"{phone}@sportive.com";
                        var phoneHash = _encryptionHelper.ComputeSearchHash(phone);
                        var emailHash = _encryptionHelper.ComputeSearchHash(generatedEmail);
                        var existing = await _db.Customers.FirstOrDefaultAsync(c => c.PhoneHash == phoneHash || c.EmailHash == emailHash);

                        if (existing != null)
                        {
                            customerId = existing.Id;
                            // Ensure account exists
                            await _customerService.EnsureCustomerAccountAsync(customerId.Value);
                        }
                        else
                        {
                            // Create new guest or walk-in customer
                            var newCust = new Customer
                            {
                                FullName = dto.CustomerName ?? (phone == "0000000000" ? "Walk-in Customer" : "Online Guest"),
                                Phone = phone,
                                Email = generatedEmail,
                                CreatedAt = now,
                                IsActive = true
                            };
                            _db.Customers.Add(newCust);
                            await _db.SaveChangesAsync(); // Try to save here to catch conflict early
                            customerId = newCust.Id;
                            await _customerService.EnsureCustomerAccountAsync(customerId.Value);
                        }
                    }
                    catch (DbUpdateException)
                    {
                        // 🛡️ Fallback: If SaveChanges failed, someone else might have just created this customer.
                        // Refresh the context and try to find them.
                        _db.ChangeTracker.Clear();
                        var fallbackEmail = $"{phone}@sportive.com";
                        var phoneHash = _encryptionHelper.ComputeSearchHash(phone);
                        var fallbackEmailHash = _encryptionHelper.ComputeSearchHash(fallbackEmail);
                        var resolved = await _db.Customers.FirstOrDefaultAsync(c => c.PhoneHash == phoneHash || c.EmailHash == fallbackEmailHash);
                        if (resolved != null) customerId = resolved.Id;
                        else throw; // If still not found, rethrow the original exception
                    }
                }

                if (!customerId.HasValue) throw new ArgumentException(_t.Get("Auth.IdentifierRequired"));

                if (dto.Source == OrderSource.POS && string.IsNullOrEmpty(dto.SalesPersonId))
                {
                    throw new ArgumentException(_t.Get("Orders.SellerRequired"));
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
                        throw new ArgumentException(_t.Get("Orders.CreditPolicyViolation"));
                    }
                }

                 var actualSource = ((int)dto.Source == 0) ? OrderSource.Website : dto.Source;

                int? branchIdToUse = dto.BranchId;
                int? warehouseIdToUse = dto.WarehouseId;

                if (!branchIdToUse.HasValue)
                {
                    if (actualSource == OrderSource.Website)
                    {
                        var webBranch = await _db.Branches.FirstOrDefaultAsync(b => b.Name == "الموقع الإلكتروني");
                        if (webBranch != null) branchIdToUse = webBranch.Id;
                    }

                    if (!branchIdToUse.HasValue)
                    {
                        var defaultBranch = await _db.Branches.FirstOrDefaultAsync(b => b.Name == "الفرع الرئيسي" || b.IsActive);
                        if (defaultBranch != null) branchIdToUse = defaultBranch.Id;
                    }
                }

                if (!warehouseIdToUse.HasValue)
                {
                    if (actualSource == OrderSource.Website && store?.WebsiteWarehouseId.HasValue == true)
                    {
                        warehouseIdToUse = store.WebsiteWarehouseId.Value;
                    }
                    else
                    {
                        var defaultWarehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.Name == "المخزن الرئيسي" || w.IsActive);
                        if (defaultWarehouse != null) warehouseIdToUse = defaultWarehouse.Id;
                    }
                }

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
                    TemporalDiscount = 0,
                    AttachmentUrl = dto.AttachmentUrl,
                    AttachmentPublicId = dto.AttachmentPublicId,
                    BranchId = branchIdToUse,
                    WarehouseId = warehouseIdToUse,
                    CreatedAt = now
                };

                var activeDiscounts = await _db.ProductDiscounts
                    .Where(d => d.IsActive && d.ValidFrom <= now && d.ValidTo >= now)
                    .Where(d => d.ApplyTo == DiscountApplyTo.All || 
                               (actualSource == OrderSource.POS && d.ApplyTo == DiscountApplyTo.POS) ||
                               (actualSource == OrderSource.Website && d.ApplyTo == DiscountApplyTo.Store))
                    .ToListAsync();

                var specialOffers = await _db.SpecialOffers
                    .Where(o => o.IsActive && o.ValidFrom <= now && o.ValidTo >= now)
                    .Where(o => o.ApplyTo == DiscountApplyTo.All || 
                               (actualSource == OrderSource.POS && o.ApplyTo == DiscountApplyTo.POS) ||
                               (actualSource == OrderSource.Website && o.ApplyTo == DiscountApplyTo.Store))
                    .ToListAsync();

                // ⚡ Preload category tree to avoid N+1 inside discount loops
                var allCategories = await _db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.ParentId);

                // 3. Handle Items
                if (dto.Items != null && dto.Items.Any())
                {
                    // ⚡ Preload all products for this order to avoid N+1 query
                    var productIds = dto.Items.Select(i => i.ProductId).Distinct().ToList();
                    var productsDict = await _db.Products.Include(p => p.Variants).Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

                    bool isCostSale = dto.Note != null && dto.Note.Contains("[CostSale]");

                    foreach (var item in dto.Items)
                    {
                        if (!productsDict.TryGetValue(item.ProductId, out var product)) continue;

                        var variant = item.ProductVariantId.HasValue 
                            ? product.Variants.FirstOrDefault(v => v.Id == item.ProductVariantId)
                            : null;

                        decimal originalUnitPrice = product.Price;
                        if (variant?.PriceAdjustment.HasValue == true)
                            originalUnitPrice += variant.PriceAdjustment.Value;

                        // ✅ FIX: If the product is tax-exclusive, scale up the original price
                        // so it matches the POS frontend which calculates totals inclusively.
                        if (product.HasTax && !product.IsTaxInclusive)
                        {
                            var appliedRate = product.VatRate ?? store?.VatRatePercent ?? 0;
                            originalUnitPrice = originalUnitPrice * (1 + (appliedRate / 100m));
                        }

                        decimal unitPrice;
                        if (actualSource == OrderSource.POS)
                        {
                            unitPrice = item.UnitPrice;
                        }
                        else
                        {
                            // 🔍 RECURSIVE DISCOUNT LOOKUP: Product -> Category Tree -> Brand
                            var disc = activeDiscounts.FirstOrDefault(d => d.ProductId == product.Id);
                            if (disc == null)
                            {
                                int? currentCatId = product.CategoryId;
                                while (currentCatId.HasValue && disc == null)
                                {
                                    int lookupId = currentCatId.Value;
                                    disc = activeDiscounts.FirstOrDefault(d => d.CategoryId == lookupId);
                                    if (disc == null)
                                    {
                                        currentCatId = allCategories.GetValueOrDefault(lookupId);
                                    }
                                }
                            }
                            if (disc == null) disc = activeDiscounts.FirstOrDefault(d => d.BrandId == product.BrandId);
                            
                            // 🌐 Fallback: Store-wide discount
                            if (disc == null) disc = activeDiscounts.FirstOrDefault(d => d.ProductId == null && d.CategoryId == null && d.BrandId == null);

                            if (disc != null && item.Quantity >= disc.MinQty)
                            {
                                unitPrice = disc.DiscountType == DiscountType.Percentage 
                                    ? Math.Round(originalUnitPrice - (originalUnitPrice * disc.DiscountValue / 100), 2)
                                    : Math.Round(originalUnitPrice - disc.DiscountValue, 2);
                            }
                            else
                            {
                                unitPrice = (product.DiscountPrice > 0 ? product.DiscountPrice.Value : originalUnitPrice);
                            }
                        }
                        
                        var orderItem = new OrderItem
                        {
                            ProductId = item.ProductId,
                            ProductVariantId = item.ProductVariantId,
                            ProductNameAr = product.NameAr,
                            ProductNameEn = product.NameEn,
                            SKU = product.SKU,
                            Size = item.Size ?? variant?.Size,
                            Color = item.Color ?? variant?.Color,
                            Quantity = item.Quantity,
                            UnitPrice = unitPrice,
                            OriginalUnitPrice = isCostSale ? unitPrice : originalUnitPrice,
                            DiscountAmount = isCostSale ? 0 : (originalUnitPrice - unitPrice) * item.Quantity,
                            TotalPrice = (actualSource == OrderSource.POS) ? item.TotalPrice : (unitPrice * item.Quantity),
                            HasTax = item.HasTax ?? product.HasTax,
                            VatRateApplied = item.VatRate ?? product.VatRate ?? (store?.VatRatePercent ?? 0),
                            CreatedAt = TimeHelper.GetEgyptTime()
                        };

                        if (orderItem.HasTax)
                        {
                            var rate = (orderItem.VatRateApplied ?? 0) / 100m;
                            decimal net = Math.Round(orderItem.TotalPrice / (1 + rate), 2);
                            orderItem.ItemVatAmount = orderItem.TotalPrice - net;
                            order.TotalVatAmount += orderItem.ItemVatAmount;
                        }

                        var availableStock = variant?.StockQuantity ?? (product.TotalStock);
                        if (store != null && !store.AllowBackorders && item.Quantity > availableStock)
                        {
                            throw new ArgumentException(
                                actualSource == OrderSource.POS
                                ? _t.Get("Orders.StockUnavailable", item.Quantity, product.NameAr, availableStock)
                                : _t.Get("Orders.StockUnavailable", item.Quantity, product.NameAr, availableStock));
                        }

                        order.Items.Add(orderItem);
                        order.SubTotal += (orderItem.OriginalUnitPrice * orderItem.Quantity);
                        order.TemporalDiscount += orderItem.DiscountAmount;

                        await _inventory.LogMovementAsync(
                            InventoryMovementType.Sale,
                            -item.Quantity,
                            item.ProductId,
                            item.ProductVariantId,
                            order.OrderNumber,
                            _t.Get("Orders.StatusCreated"),
                            order.SalesPersonId,
                            0, // unitCost fallback
                            order.Source,
                            autoSave: false,
                            warehouseId: warehouseIdToUse
                        );
                    }
                }
                else
                {
                    var cartItems = await _db.CartItems.Include(c => c.Product).ThenInclude(p => p!.Variants)
                        .Include(c => c.ProductVariant)
                        .Where(c => c.CustomerId == customerId.Value).ToListAsync();
                    
                    if (!cartItems.Any()) throw new ArgumentException(_t.Get("Orders.CartEmpty"));

                    foreach (var ci in cartItems)
                    {
                    if (ci.Product == null) continue;
                    var availableStock = ci.ProductVariant?.StockQuantity ?? ci.Product.TotalStock;
                    if (store != null && !store.AllowBackorders && ci.Quantity > availableStock)
                    {
                        throw new ArgumentException(_t.Get("Orders.StockUnavailable", ci.Quantity, ci.Product.NameAr, availableStock));
                    }

                        decimal originalUnitPrice = ci.Product.Price;
                        if (ci.ProductVariant?.PriceAdjustment.HasValue == true)
                            originalUnitPrice += ci.ProductVariant.PriceAdjustment.Value;

                            // 🔍 RECURSIVE DISCOUNT LOOKUP: Product -> Category Tree -> Brand
                            var disc = activeDiscounts.FirstOrDefault(d => d.ProductId == ci.ProductId);
                            if (disc == null)
                            {
                                int? currentCatId = ci.Product.CategoryId;
                                while (currentCatId.HasValue && disc == null)
                                {
                                    int lookupId = currentCatId.Value;
                                    disc = activeDiscounts.FirstOrDefault(d => d.CategoryId == lookupId);
                                    if (disc == null)
                                    {
                                        currentCatId = allCategories.GetValueOrDefault(lookupId);
                                    }
                                }
                            }
                            if (disc == null) disc = activeDiscounts.FirstOrDefault(d => d.BrandId == ci.Product.BrandId);
                            
                            // 🌐 Fallback: Store-wide discount
                            if (disc == null) disc = activeDiscounts.FirstOrDefault(d => d.ProductId == null && d.CategoryId == null && d.BrandId == null);

                            decimal unitPrice;
                            if (disc != null && ci.Quantity >= disc.MinQty)
                        {
                            unitPrice = disc.DiscountType == DiscountType.Percentage 
                                ? Math.Round(originalUnitPrice - (originalUnitPrice * disc.DiscountValue / 100), 2)
                                : Math.Round(originalUnitPrice - disc.DiscountValue, 2);
                        }
                        else
                        {
                            unitPrice = (ci.Product.DiscountPrice > 0 ? ci.Product.DiscountPrice.Value : originalUnitPrice);
                        }
                        
                        var orderItem = new OrderItem
                        {
                            ProductId = ci.ProductId,
                            Product = ci.Product, // Preserve for special offers logic
                            ProductVariantId = ci.ProductVariantId,
                            ProductNameAr = ci.Product.NameAr,
                            ProductNameEn = ci.Product.NameEn,
                            SKU = ci.Product.SKU,
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
                        order.TemporalDiscount += orderItem.DiscountAmount;

                        await _inventory.LogMovementAsync(
                            InventoryMovementType.Sale,
                            -ci.Quantity,
                            ci.ProductId,
                            ci.ProductVariantId,
                            order.OrderNumber,
                            _t.Get("Orders.StatusCreated"),
                            null,
                            0, // unitCost fallback
                            order.Source,
                            autoSave: false,
                            warehouseId: warehouseIdToUse
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

                // 🎁 NEW: Special Bundle/Quantity Offers Logic
                if (specialOffers.Any())
                {
                    foreach (var offer in specialOffers)
                    {
                        // 🎯 Filter items eligible for this specific offer (Categories/Brands)
                        var eligibleItems = order.Items
                            .Where(i => {
                                bool matchCategory = string.IsNullOrEmpty(offer.EligibleCategoryIds);
                                if (!matchCategory && i.Product != null)
                                {
                                    var eligibleIds = offer.EligibleCategoryIds!.Split(',')
                                        .Where(s => !string.IsNullOrEmpty(s))
                                        .Select(int.Parse).ToList();
                                    
                                    int? currentCatId = i.Product.CategoryId;
                                    while (currentCatId.HasValue)
                                    {
                                        if (eligibleIds.Contains(currentCatId.Value))
                                        {
                                            matchCategory = true;
                                            break;
                                        }
                                        currentCatId = allCategories.GetValueOrDefault(currentCatId.Value);
                                    }
                                }
                                
                                bool matchBrand = string.IsNullOrEmpty(offer.EligibleBrandIds) || 
                                    (i.Product != null && offer.EligibleBrandIds.Split(',').Contains(i.Product.BrandId.ToString()));
                                
                                return matchCategory && matchBrand;
                            })
                            .SelectMany(i => Enumerable.Repeat(i, i.Quantity))
                            .ToList();
                        
                        int countToDiscount = 0;
                        if (offer.FreeQuantity.HasValue && offer.FreeQuantity.Value > 0)
                        {
                            // 🔄 REPEATING BUNDLE LOGIC (e.g. Buy 3 get 7 free -> Bundle of 10)
                            int bundleSize = offer.ThresholdQuantity + offer.FreeQuantity.Value;
                            if (bundleSize > 0)
                            {
                                int bundlesCount = eligibleItems.Count / bundleSize;
                                countToDiscount = bundlesCount * offer.FreeQuantity.Value;
                            }
                        }
                        else if (eligibleItems.Count > offer.ThresholdQuantity)
                        {
                            // 📈 SIMPLE THRESHOLD LOGIC (Everything after piece X is discounted)
                            countToDiscount = eligibleItems.Count - offer.ThresholdQuantity;
                        }

                        if (countToDiscount > 0)
                        {
                            // Sort by UnitPrice (cheapest first) to apply discount to the lowest price items
                            var sortedItems = eligibleItems.OrderBy(i => i.UnitPrice).ToList();
                            decimal offerDiscount = 0;
                            
                            for (int i = 0; i < countToDiscount; i++)
                            {
                                var item = sortedItems[i];
                                decimal discPercentage = offer.IsFullDiscount ? 100 : offer.DiscountPercentage;
                                decimal discountPerPiece = Math.Round(item.UnitPrice * (discPercentage / 100m), 2);
                                
                                // ✅ Update item totals for accounting accuracy
                                item.TotalPrice -= discountPerPiece;
                                item.DiscountAmount += discountPerPiece;

                                if (item.HasTax)
                                {
                                    var rate = (item.VatRateApplied ?? 14) / 100m;
                                    decimal newNet = Math.Round(item.TotalPrice / (1 + rate), 2);
                                    decimal oldVat = item.ItemVatAmount;
                                    item.ItemVatAmount = item.TotalPrice - newNet;
                                    
                                    // Update global order VAT
                                    order.TotalVatAmount += (item.ItemVatAmount - oldVat);
                                }

                                offerDiscount += discountPerPiece;
                            }
                            
                            order.TemporalDiscount += Math.Round(offerDiscount, 2);
                            
                            // Note: We only apply one bundle offer per order for now (the first one found)
                            break; 
                        }
                    }
                }

                // 🛡️ Priority Logic: Temporal Discount (Offers) > Manual/Coupon Discount
                if (order.TemporalDiscount > 0)
                {
                    order.DiscountAmount = 0;
                    order.CouponCode = null;
                }
                else
                {
                    order.DiscountAmount += (dto.DiscountAmount ?? 0);
                }

                order.TotalAmount = Math.Max(0, order.SubTotal + order.DeliveryFee - order.DiscountAmount - order.TemporalDiscount);

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

                        // 📝 SMART NOTES: Append payment breakdown to AdminNotes for easier visibility
                        if (dto.Payments.Count > 1 || (dto.Payments.Any(p => p.Method == PaymentMethod.Credit)))
                        {
                            var breakdown = string.Join(" | ", dto.Payments.Select(p => $"{p.Method}: {p.Amount}"));
                            order.AdminNotes = string.IsNullOrEmpty(order.AdminNotes) ? breakdown : $"{order.AdminNotes} | {breakdown}";
                        }
                        
                        if (order.PaidAmount > order.TotalAmount + 0.1m)
                            throw new ArgumentException(_t.Get("Orders.OverpaidError", order.PaidAmount, order.TotalAmount));

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
                    throw new ArgumentException(_t.Get("Orders.MinAmountError", store.MinOrderAmount, order.TotalAmount));
                }

                if (store != null && !store.AllowBackorders)
                {
                     // Stock specifically already checked for each item, but we double-check here if it was a website order with no stock
                }
                _db.Orders.Add(order);
                order.StatusHistory.Add(new OrderStatusHistory { 
                    Status = order.Status, 
                    CreatedAt = TimeHelper.GetEgyptTime(), 
                    Note = _t.Get("Orders.StatusCreated"),
                    ChangedByUserId = dto.SalesPersonId
                });

                if (isCreditOrder)
                {
                    decimal creditAmount = order.TotalAmount - order.PaidAmount;
                    if (creditAmount > 0)
                    {
                        var installment = new CustomerInstallment
                        {
                            CustomerId = order.CustomerId,
                            Order = order,
                            TotalAmount = creditAmount,
                            PaidAmount = 0,
                            DueDate = now.AddDays(30),
                            Note = $"قسط تلقائي للفاتورة رقم {order.OrderNumber} (Auto installment for order {order.OrderNumber})",
                            Status = InstallmentStatus.Pending,
                            CreatedAt = now
                        };
                        _db.CustomerInstallments.Add(installment);
                    }
                }

                // ✅ UPDATE COUPON USAGE
                if (!string.IsNullOrEmpty(order.CouponCode))
                {
                    var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == order.CouponCode.ToUpper());
                    if (coupon != null)
                    {
                        if (coupon.MaxUsageCount.HasValue && coupon.CurrentUsageCount >= coupon.MaxUsageCount)
                            throw new ArgumentException(_t.Get("Orders.CouponLimitReached"));

                        coupon.CurrentUsageCount++;
                    }
                }

                // ⚡ Outbox Trigger: Save event in the same transaction
                _dashboardEvents.NotifyTransactionOccurred(order.CreatedAt);

                // 🇸🇦 Process ZATCA/ETA Taxes locally before save
                await _taxIntegrationService.ProcessOrderTaxesAsync(order);

                await _db.SaveChangesAsync();

                // 💳 CUSTOMER BALANCE DEDUCTION: When paying via stored balance, we must
                // update the customer's TotalSales immediately to reflect the new order.
                // We do NOT update TotalPaid because no new cash was received (it consumes existing balance).
                if (order.PaymentMethod == PaymentMethod.CustomerBalance && order.PaidAmount > 0)
                {
                    var custToUpdate = await _db.Customers.FindAsync(customerId.Value);
                    if (custToUpdate != null)
                    {
                        custToUpdate.TotalSales += order.TotalAmount;
                        await _db.SaveChangesAsync();
                    }
                }

                // 💳 ATOMIC ACCOUNTING: Post to ledger BEFORE committing the transaction
                // This ensures order and accounting are 1:1 and either both succeed or both fail.
                await _accounting.PostSalesOrderAsync(order);

                await tx.CommitAsync();

                // ⚡ Immediate Wake-up: Trigger outbox processing now
                _dashboardEvents.TriggerImmediateProcessing();

                return (await GetOrderByIdAsync(order.Id))!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Atomic order creation failed for {OrderNo}", order?.OrderNumber);
                await tx.RollbackAsync(); 
                throw; 
            }
        });

        // ✅ Background tasks: bypass Hangfire to avoid MySQL lock contention / timeouts on Hostinger during critical checkout flow
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var customerService = scope.ServiceProvider.GetRequiredService<ICustomerService>();
                await customerService.EvaluateCustomerCategoryAsync(customerId.GetValueOrDefault());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background customer category evaluation failed for {CustomerId}", customerId);
            }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await SendOrderNotificationsAsync(result.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background order notification failed for order {OrderId}", result.Id);
            }
        });

        return result;
    }


    public async Task<OrderDetailDto> UpdateOrderAsync(int orderId, UpdateOrderDto dto, string updatedByUserId)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .Include(o => o.Customer)
            .Include(o => o.DeliveryAddress)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null) throw new KeyNotFoundException(_t.Get("Orders.NotFound"));

        var strategy = _db.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var now = TimeHelper.GetEgyptTime();
                var store = await _db.StoreInfo.FirstOrDefaultAsync(s => s.StoreConfigId == 1);

                // 1. CALCULATE DIFFS FOR INVENTORY
                var oldItemSummary = order.Items
                    .Where(i => i.ProductId > 0)
                    .GroupBy(i => new { ProductId = (int)i.ProductId!, VariantId = i.ProductVariantId ?? 0 })
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

                var newItemSummary = dto.Items
                    .Where(i => i.ProductId > 0)
                    .GroupBy(i => new { ProductId = (int)i.ProductId, VariantId = i.ProductVariantId ?? 0 })
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

                var allKeys = oldItemSummary.Keys.Union(newItemSummary.Keys).Distinct().ToList();

                // 2. STOCK VALIDATION (For positive diffs only)
                foreach (var key in allKeys)
                {
                    int diff = newItemSummary.GetValueOrDefault(key) - oldItemSummary.GetValueOrDefault(key);
                    if (diff > 0)
                    {
                        if (key.VariantId > 0)
                        {
                            var variantStock = await _db.ProductVariants
                                .Where(v => v.Id == key.VariantId)
                                .Select(v => new { v.StockQuantity, v.Size, v.ColorAr, v.Color, Product = v.Product != null ? v.Product.NameAr : "" })
                                .FirstOrDefaultAsync();

                            if (variantStock != null && variantStock.StockQuantity < diff)
                            {
                                throw new InvalidOperationException($"لا يمكن تعديل الفاتورة: الكمية المطلوبة الإضافية ({diff}) من '{variantStock.Product}' (مقاس {variantStock.Size} / {variantStock.ColorAr ?? variantStock.Color}) تتجاوز المخزون المتاح ({variantStock.StockQuantity}).");
                            }
                        }
                        else
                        {
                            var productStock = await _db.Products
                                .Where(p => p.Id == key.ProductId)
                                .Select(p => new { p.TotalStock, p.NameAr })
                                .FirstOrDefaultAsync();

                            if (productStock != null && productStock.TotalStock < diff)
                            {
                                throw new InvalidOperationException($"لا يمكن تعديل الفاتورة: الكمية المطلوبة الإضافية ({diff}) من '{productStock.NameAr}' تتجاوز المخزون المتاح ({productStock.TotalStock}).");
                            }
                        }
                    }
                }

                // 3. LOG INVENTORY MOVEMENTS (Diffs only)
                foreach (var key in allKeys)
                {
                    int diff = newItemSummary.GetValueOrDefault(key) - oldItemSummary.GetValueOrDefault(key);
                    if (diff > 0)
                    {
                        await _inventory.LogMovementAsync(InventoryMovementType.Sale, -diff, key.ProductId, key.VariantId == 0 ? (int?)null : key.VariantId, order.OrderNumber, "Order Edited (Item Added or Increased)", updatedByUserId, 0, order.Source, false, true);
                    }
                    else if (diff < 0)
                    {
                        await _inventory.LogMovementAsync(InventoryMovementType.Adjustment, -diff, key.ProductId, key.VariantId == 0 ? (int?)null : key.VariantId, order.OrderNumber, "Order Edited (Item Removed or Decreased)", updatedByUserId, 0, order.Source, false, true);
                    }
                }

                // 3.5. CLEAR OLD PAYMENTS (Will be recreated below)
                _db.OrderPayments.RemoveRange(order.Payments);
                order.Payments.Clear();

                // 4. UPDATE CUSTOMER IF CHANGED
                if (dto.CustomerId.HasValue && dto.CustomerId != order.CustomerId)
                {
                    order.CustomerId = dto.CustomerId.Value;
                }

                // 5. UPDATE ORDER ITEMS 
                var productIds = dto.Items.Select(i => i.ProductId).Union(order.Items.Select(i => i.ProductId ?? 0)).Where(id => id > 0).Distinct().ToList();
                var productsDict = await _db.Products.Include(p => p.Variants).Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
                
                var diffLines = new List<string>();
                foreach (var key in allKeys)
                {
                    int oldQty = oldItemSummary.GetValueOrDefault(key);
                    int newQty = newItemSummary.GetValueOrDefault(key);
                    if (oldQty != newQty)
                    {
                        string prodName = "منتج";
                        if (productsDict.TryGetValue(key.ProductId, out var p))
                        {
                            prodName = p.NameAr;
                            if (key.VariantId > 0)
                            {
                                var variant = p.Variants.FirstOrDefault(v => v.Id == key.VariantId);
                                if (variant != null) prodName += $" ({variant.Size} / {variant.ColorAr ?? variant.Color})";
                            }
                        }
                        
                        if (oldQty == 0) diffLines.Add($"إضافة [{prodName}] (كمية: {newQty})");
                        else if (newQty == 0) diffLines.Add($"حذف [{prodName}]");
                        else diffLines.Add($"[{prodName}] من {oldQty} إلى {newQty}");
                    }
                }
                string editNote = diffLines.Any() 
                    ? "تعديلات: " + string.Join(" ، ", diffLines)
                    : "تم تعديل بيانات الفاتورة (بدون تعديل كميات)";
                bool isCostSale = dto.AdminNotes != null && dto.AdminNotes.Contains("[CostSale]");

                order.SubTotal = 0;
                order.TemporalDiscount = 0;
                order.TotalVatAmount = 0;

                var existingItemsPool = order.Items.ToList(); 

                foreach (var itemDto in dto.Items)
                {
                    if (!productsDict.TryGetValue(itemDto.ProductId, out var product)) continue;

                    var variant = itemDto.ProductVariantId.HasValue 
                        ? product.Variants.FirstOrDefault(v => v.Id == itemDto.ProductVariantId)
                        : null;

                    decimal originalUnitPrice = isCostSale ? itemDto.UnitPrice : (product.Price + (variant?.PriceAdjustment ?? 0));
                    decimal discountAmount = isCostSale ? 0 : (originalUnitPrice - itemDto.UnitPrice) * itemDto.Quantity;
                    decimal totalPrice = itemDto.TotalPrice > 0 ? itemDto.TotalPrice : (itemDto.UnitPrice * itemDto.Quantity);
                    bool hasTax = itemDto.HasTax ?? product.HasTax;
                    decimal vatRateApplied = itemDto.VatRate ?? product.VatRate ?? (store?.VatRatePercent ?? 14);

                    var existingItem = existingItemsPool.FirstOrDefault(i => i.ProductId == itemDto.ProductId && i.ProductVariantId == itemDto.ProductVariantId);
                    
                    if (existingItem != null)
                    {
                        if (itemDto.Quantity < existingItem.ReturnedQuantity)
                        {
                            throw new InvalidOperationException($"لا يمكن تقليل كمية '{product.NameAr}' لأقل من الكمية المرتجعة ({existingItem.ReturnedQuantity}).");
                        }
                        
                        // Update existing item
                        existingItem.Quantity = itemDto.Quantity;
                        existingItem.UnitPrice = itemDto.UnitPrice;
                        existingItem.OriginalUnitPrice = originalUnitPrice;
                        existingItem.DiscountAmount = discountAmount;
                        existingItem.TotalPrice = totalPrice;
                        existingItem.HasTax = hasTax;
                        existingItem.VatRateApplied = vatRateApplied;
                        existingItem.ProductNameAr = product.NameAr;
                        existingItem.ProductNameEn = product.NameEn;
                        existingItem.SKU = product.SKU;
                        existingItem.Size = variant?.Size;
                        existingItem.Color = variant?.Color;

                        if (existingItem.HasTax)
                        {
                            var rate = (existingItem.VatRateApplied ?? 14) / 100m;
                            decimal net = Math.Round(existingItem.TotalPrice / (1 + rate), 2);
                            existingItem.ItemVatAmount = existingItem.TotalPrice - net;
                            order.TotalVatAmount += existingItem.ItemVatAmount;
                        }

                        order.SubTotal += (existingItem.OriginalUnitPrice * existingItem.Quantity);
                        order.TemporalDiscount += existingItem.DiscountAmount;
                        
                        existingItemsPool.Remove(existingItem);
                    }
                    else
                    {
                        var orderItem = new OrderItem
                        {
                            ProductId = itemDto.ProductId,
                            ProductVariantId = itemDto.ProductVariantId,
                            ProductNameAr = product.NameAr,
                            ProductNameEn = product.NameEn,
                            SKU = product.SKU,
                            Size = variant?.Size,
                            Color = variant?.Color,
                            Quantity = itemDto.Quantity,
                            UnitPrice = itemDto.UnitPrice,
                            OriginalUnitPrice = originalUnitPrice,
                            DiscountAmount = discountAmount,
                            TotalPrice = totalPrice,
                            HasTax = hasTax,
                            VatRateApplied = vatRateApplied,
                            CreatedAt = now
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
                        order.TemporalDiscount += orderItem.DiscountAmount;
                    }
                }

                // Remove the ones that weren't matched
                foreach (var itemToRemove in existingItemsPool)
                {
                    if (itemToRemove.ReturnedQuantity > 0)
                    {
                        throw new InvalidOperationException($"لا يمكن حذف '{itemToRemove.ProductNameAr}' لأنه يحتوي على كمية مرتجعة ({itemToRemove.ReturnedQuantity}).");
                    }
                    order.Items.Remove(itemToRemove);
                    _db.OrderItems.Remove(itemToRemove);
                }

                // 5. UPDATE TOTALS
                order.DiscountAmount = dto.DiscountAmount;
                order.AdminNotes = dto.AdminNotes;
                order.TotalAmount = Math.Max(0, order.SubTotal + order.DeliveryFee - order.DiscountAmount - order.TemporalDiscount);

                // 6. UPDATE PAYMENTS
                if (dto.Payments != null && dto.Payments.Any())
                {
                    order.Payments.Clear();
                    decimal totalPaid = 0;
                    foreach (var p in dto.Payments)
                    {
                        if (p.Amount <= 0 || p.Method == PaymentMethod.Credit) continue;
                        totalPaid += p.Amount;
                        order.Payments.Add(new OrderPayment { Method = p.Method, Amount = p.Amount, CreatedAt = now });
                    }
                    order.PaidAmount = totalPaid;
                }
                else if (dto.PaidAmount.HasValue)
                {
                    // 🚨 IMPORTANT: Clear old payments to avoid accumulation when admin edits the total paid
                    order.Payments.Clear();
                    order.PaidAmount = dto.PaidAmount.Value;
                    
                    var method = dto.PaymentMethod ?? order.PaymentMethod;
                    if (method != PaymentMethod.Credit && order.PaidAmount > 0)
                    {
                         order.Payments.Add(new OrderPayment { Method = method, Amount = order.PaidAmount, CreatedAt = now });
                    }
                }

                // Update Payment Status
                if (order.PaidAmount >= order.TotalAmount - 0.01m) order.PaymentStatus = PaymentStatus.Paid;
                else if (order.PaidAmount > 0) order.PaymentStatus = PaymentStatus.PartiallyPaid;
                else order.PaymentStatus = PaymentStatus.Pending;

                // 5b. UPDATE DATE if provided (sync journal entries + receipt vouchers)
                if (dto.CreatedAt.HasValue)
                {
                    order.CreatedAt = dto.CreatedAt.Value;
                    // Note: journal entries are deleted and re-posted below, so we don't need to update them here
                    // Receipt vouchers date sync (if any exist — unlikely since they're created fresh)
                    var existingVouchers = await _db.ReceiptVouchers.Where(v => v.OrderId == order.Id).ToListAsync();
                    foreach (var v in existingVouchers)
                    {
                        v.VoucherDate = TimeHelper.GetEgyptBusinessDayDate(dto.CreatedAt.Value);
                        v.CreatedAt   = dto.CreatedAt.Value;
                    }
                }

                order.UpdatedAt = now;
                order.StatusHistory.Add(new OrderStatusHistory 
                { 
                    Status = order.Status, 
                    Note = editNote, 
                    ChangedByUserId = updatedByUserId, 
                    CreatedAt = now 
                });

                // 7. SYNC ACCOUNTING: Delete old entries first
                // We find all entries related to this order by ID or Reference (Invoice, Payments, Returns)
                var oldEntries = await _db.JournalEntries
                    .Where(e => e.OrderId == order.Id || e.Reference == order.OrderNumber || (e.Reference != null && e.Reference.StartsWith(order.OrderNumber + "-")))
                    .ToListAsync();

                if (oldEntries.Any())
                {
                    var isSameBusinessDay = TimeHelper.GetEgyptBusinessDayDate(order.CreatedAt) == TimeHelper.GetEgyptBusinessDayDate(now);

                    if (isSameBusinessDay)
                    {
                        var entryIds = oldEntries.Select(e => e.Id).ToList();
                        
                        // 🛡️ UNLINK VOUCHERS: Prevent FK violations (Receipt/Payment vouchers linked to these entries)
                        await _db.ReceiptVouchers
                            .Where(v => v.JournalEntryId.HasValue && entryIds.Contains(v.JournalEntryId.Value))
                            .ExecuteUpdateAsync(s => s.SetProperty(v => v.JournalEntryId, (int?)null));

                        await _db.PaymentVouchers
                            .Where(v => v.JournalEntryId.HasValue && entryIds.Contains(v.JournalEntryId.Value))
                            .ExecuteUpdateAsync(s => s.SetProperty(v => v.JournalEntryId, (int?)null));

                        // 🛡️ UNLINK REVERSALS: Prevent FK violations if any of these entries were reversed
                        await _db.JournalEntries
                            .Where(e => e.ReversalOfId.HasValue && entryIds.Contains(e.ReversalOfId.Value))
                            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ReversalOfId, (int?)null));

                        var lines = await _db.JournalLines.Where(l => entryIds.Contains(l.JournalEntryId)).ToListAsync();
                        _db.JournalLines.RemoveRange(lines);
                        _db.JournalEntries.RemoveRange(oldEntries);
                    }
                    else
                    {
                        // 🔄 REVERSAL & REPOSTING FOR PREVIOUS DAYS
                        // To avoid altering closed shifts and past daily reports, we reverse the old entries TODAY instead of deleting them.
                        var entriesToReverse = oldEntries.Where(e => e.Status != JournalEntryStatus.Reversed).ToList();
                        foreach (var e in entriesToReverse)
                        {
                            await _accounting.ReverseEntryAsync(e.Id, $"تعديل الفاتورة رقم {order.OrderNumber}");
                        }
                    }
                }

                // 💳 SYNC INSTALLMENT ON ORDER UPDATE
                bool isCreditOrderUpdated = (dto.PaymentMethod == PaymentMethod.Credit) || 
                                            (dto.Payments != null && dto.Payments.Any(p => p.Method == PaymentMethod.Credit)) || 
                                            (order.PaymentMethod == PaymentMethod.Credit);

                var existingInstallment = await _db.CustomerInstallments
                    .FirstOrDefaultAsync(i => i.OrderId == order.Id && i.Status != InstallmentStatus.Cancelled);

                if (isCreditOrderUpdated)
                {
                    decimal creditAmount = order.TotalAmount - order.PaidAmount;
                    if (creditAmount > 0)
                    {
                        if (existingInstallment != null)
                        {
                            existingInstallment.CustomerId = order.CustomerId;
                            existingInstallment.TotalAmount = creditAmount;
                            existingInstallment.UpdatedAt = now;
                        }
                        else
                        {
                            var installment = new CustomerInstallment
                            {
                                CustomerId = order.CustomerId,
                                OrderId = order.Id,
                                TotalAmount = creditAmount,
                                PaidAmount = 0,
                                DueDate = now.AddDays(30),
                                Note = $"قسط تلقائي للفاتورة رقم {order.OrderNumber} (Auto installment for order {order.OrderNumber})",
                                Status = InstallmentStatus.Pending,
                                CreatedAt = now
                            };
                            _db.CustomerInstallments.Add(installment);
                        }
                    }
                    else if (existingInstallment != null)
                    {
                        _db.CustomerInstallments.Remove(existingInstallment);
                    }
                }
                else if (existingInstallment != null)
                {
                    _db.CustomerInstallments.Remove(existingInstallment);
                }

                await _db.SaveChangesAsync();

                // POST NEW ACCOUNTING
                var isSameBusinessDayCheck = TimeHelper.GetEgyptBusinessDayDate(order.CreatedAt) == TimeHelper.GetEgyptBusinessDayDate(now);
                DateTime? overrideDate = isSameBusinessDayCheck ? null : now;

                await _accounting.PostSalesOrderAsync(order, overrideDate);
                if (order.PaidAmount > 0)
                {
                    await _accounting.PostOrderPaymentAsync(order, overrideDate);
                }

                await tx.CommitAsync();
                return (await GetOrderByIdAsync(order.Id))!;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "UpdateOrderAsync failed for {OrderNo}", order.OrderNumber);
                throw new InvalidOperationException($"Error updating order {order.OrderNumber}: {ex.Message}. {(ex.InnerException != null ? "Inner: " + ex.InnerException.Message : "")}");
            }
        });

        return result;
    }

    [Queue("critical")]
    public async Task SendOrderNotificationsAsync(int orderId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var order = await db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.DeliveryAddress)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) return;

        // 1. App Notification
        if (order.Customer != null && !string.IsNullOrEmpty(order.Customer.AppUserId))
        {
            await notificationService.SendAsync(order.Customer.AppUserId, 
                "تم استلام طلبك", "Order Received",
                $"طلبك رقم {order.OrderNumber} قيد الانتظار.", $"Your order #{order.OrderNumber} is pending.",
                "Order", order.Id);
        }

        // 1.5 Admin App Notification
        await notificationService.SendAsync(null,
            $"طلب جديد: {order.OrderNumber}", "New Order",
            $"تم استلام طلب جديد رقم {order.OrderNumber} بقيمة {order.TotalAmount:N2} ج.م",
            $"New order received #{order.OrderNumber} with amount {order.TotalAmount:N2} EGP",
            order.Source == OrderSource.POS ? "POSOrder" : "OnlineOrder", order.Id);

        // 2. Admin Email Alert
        try 
        {
            var configEmails = config["Backup:Email:To"] ?? "";
            var adminEmails = $"{configEmails},abdullah.taha574@gmail.com"
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim())
                .Distinct()
                .ToList();
            var subject = $"🔔 طلب جديد: {order.OrderNumber}";
            var itemsHtml = string.Join("", order.Items.Select(i => $@"
                <tr>
                    <td style='padding: 10px; border-bottom: 1px solid #eee; text-align: right;'>{i.ProductNameAr} {(string.IsNullOrEmpty(i.Size) ? "" : $"({i.Size})")} {(string.IsNullOrEmpty(i.Color) ? "" : $"[{i.Color}]")}</td>
                    <td style='padding: 10px; border-bottom: 1px solid #eee; text-align: center;'>{i.Quantity}</td>
                    <td style='padding: 10px; border-bottom: 1px solid #eee; text-align: left;'>{i.UnitPrice:N2} ج.م</td>
                </tr>"));

            var addressHtml = "";
            if (order.FulfillmentType == FulfillmentType.Delivery && order.DeliveryAddress != null)
            {
                addressHtml = $@"
                    <div style='background: #fff; border: 1px solid #eee; padding: 15px; border-radius: 8px; margin-bottom: 20px;'>
                        <h4 style='margin-top: 0; color: #0f3460;'>📍 عنوان التوصيل</h4>
                        <p style='margin: 5px 0;'>{order.DeliveryAddress.City}, {order.DeliveryAddress.District}</p>
                        <p style='margin: 5px 0;'>{order.DeliveryAddress.Street}, مبنى {order.DeliveryAddress.BuildingNo}</p>
                        {(string.IsNullOrEmpty(order.DeliveryAddress.Floor) ? "" : $"<p style='margin: 5px 0;'>الدور {order.DeliveryAddress.Floor}, شقة {order.DeliveryAddress.ApartmentNo}</p>")}
                    </div>";
            }

            var body = $@"
                <div dir='rtl' style='font-family: Arial, sans-serif; background-color: #f6f9fc; padding: 20px;'>
                    <div style='max-width: 600px; margin: auto; background: white; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 10px rgba(0,0,0,0.05);'>
                        <div style='background: #0f3460; color: white; padding: 25px; text-align: center;'>
                            <h2 style='margin: 0;'>طلب جديد من المتجر 🆕</h2>
                            <p style='margin: 10px 0 0; opacity: 0.8;'>رقم الطلب: {order.OrderNumber}</p>
                        </div>

                        <div style='padding: 25px;'>
                            <div style='display: flex; justify-content: space-between; margin-bottom: 20px;'>
                                <div>
                                    <h4 style='margin: 0 0 5px; color: #666;'>العميل</h4>
                                    <p style='margin: 0; font-weight: bold;'>{order.Customer?.FullName ?? "عميل خارجي"}</p>
                                    <p style='margin: 3px 0; color: #888;'>{order.Customer?.Phone ?? ""}</p>
                                </div>
                                <div style='text-align: left;'>
                                    <h4 style='margin: 0 0 5px; color: #666;'>التفاصيل</h4>
                                    <p style='margin: 0; font-size: 13px;'><b>المصدر:</b> {order.Source}</p>
                                    <p style='margin: 3px 0; font-size: 13px;'><b>النوع:</b> {order.FulfillmentType}</p>
                                    <p style='margin: 3px 0; font-size: 13px;'><b>الدفع:</b> {order.PaymentMethod} ({order.PaymentStatus})</p>
                                    <p style='margin: 3px 0; font-size: 13px;'><b>التاريخ:</b> {order.CreatedAt:yyyy-MM-dd}</p>
                                </div>
                            </div>

                            {(string.IsNullOrEmpty(order.CustomerNotes) ? "" : $@"
                            <div style='background: #fff8e1; border: 1px solid #ffe082; padding: 15px; border-radius: 8px; margin-bottom: 20px;'>
                                <h4 style='margin-top: 0; color: #f57f17; font-size: 14px;'>📝 ملاحظات العميل</h4>
                                <p style='margin: 5px 0; font-size: 14px;'>{order.CustomerNotes}</p>
                            </div>")}

                            {addressHtml}

                            <table style='width: 100%; border-collapse: collapse; margin-bottom: 20px;'>
                                <thead>
                                    <tr style='background: #f8f9fa;'>
                                        <th style='padding: 12px 10px; text-align: right; font-size: 13px;'>المنتج</th>
                                        <th style='padding: 12px 10px; text-align: center; font-size: 13px;'>الكمية</th>
                                        <th style='padding: 12px 10px; text-align: left; font-size: 13px;'>السعر</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {itemsHtml}
                                </tbody>
                            </table>

                            <div style='background: #f8f9fa; padding: 15px; border-radius: 8px;'>
                                <div style='display: flex; justify-content: space-between; margin-bottom: 8px;'>
                                    <span>المجموع الفرعي:</span>
                                    <span>{order.SubTotal:N2} ج.م</span>
                                </div>
                                {(order.DeliveryFee > 0 ? $@"<div style='display: flex; justify-content: space-between; margin-bottom: 8px;'>
                                    <span>مصاريف الشحن:</span>
                                    <span>{order.DeliveryFee:N2} ج.م</span>
                                </div>" : "")}
                                {(order.DiscountAmount + order.TemporalDiscount > 0 ? $@"<div style='display: flex; justify-content: space-between; margin-bottom: 8px; color: #e94560;'>
                                    <span>إجمالي الخصم:</span>
                                    <span>-{(order.DiscountAmount + order.TemporalDiscount):N2} ج.م</span>
                                </div>" : "")}
                                <div style='display: flex; justify-content: space-between; margin-top: 10px; padding-top: 10px; border-top: 2px solid #eee; font-size: 18px; font-weight: bold; color: #0f3460;'>
                                    <span>الإجمالي:</span>
                                    <span>{order.TotalAmount:N2} ج.م</span>
                                </div>
                            </div>

                            <div style='margin-top: 30px; text-align: center;'>
                                <a href='https://sportive-sportwear.com/admin/orders?search={order.OrderNumber}&viewId={order.Id}' 
                                   style='display: inline-block; background: #0f3460; color: white; padding: 14px 35px; text-decoration: none; border-radius: 10px; font-weight: bold; font-size: 16px;'>
                                   إدارة الطلب في لوحة التحكم
                                </a>
                            </div>
                        </div>
                        
                        <div style='background: #f1f4f7; padding: 15px; text-align: center; font-size: 12px; color: #888;'>
                            هذا الإيميل مرسل آلياً من نظام Sportive.
                        </div>
                    </div>
                </div>";

            foreach (var email in adminEmails)
            {
                await emailService.SendEmailAsync(email.Trim(), subject, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send admin notification for order {OrderNo}", order.OrderNumber);
        }

        // 3. Automated WhatsApp via Wapilot
        try
        {
            var storeSettings = await db.StoreInfo.AsNoTracking().FirstOrDefaultAsync(s => s.StoreConfigId == 1);
            if (storeSettings != null && storeSettings.AutoSendWhatsAppInvoices 
                && !string.IsNullOrEmpty(storeSettings.WapilotApiKey) 
                && order.Customer != null && !string.IsNullOrEmpty(order.Customer.Phone))
            {
                string? instanceIdToUse = order.Source == OrderSource.POS 
                    ? storeSettings.WapilotPosInstanceId 
                    : storeSettings.WapilotWebInstanceId;

                if (!string.IsNullOrEmpty(instanceIdToUse))
                {
                    var waMeService = scope.ServiceProvider.GetRequiredService<IWaMeService>();
                    var waApiService = scope.ServiceProvider.GetRequiredService<IWhatsAppApiService>();

                    var waMeResult = waMeService.OrderConfirmation(order);
                    if (!string.IsNullOrEmpty(waMeResult.FullMessage))
                    {
                        await waApiService.SendWapilotMessageAsync(
                            order.Customer.Phone, 
                            waMeResult.FullMessage, 
                            storeSettings.WapilotApiKey, 
                            instanceIdToUse);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-send WhatsApp for order {OrderNo}", order.OrderNumber);
        }
    }

    public async Task<OrderDetailDto> UpdateOrderStatusAsync(int orderId, UpdateOrderStatusDto dto, string updatedByUserId)
    {
        var order = await _db.Orders.Include(o => o.StatusHistory).Include(o => o.Payments).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw new KeyNotFoundException("Order not found.");

        var oldStatus = order.Status;
        order.Status = dto.Status;
        order.UpdatedAt = TimeHelper.GetEgyptTime();
        order.StatusHistory.Add(new OrderStatusHistory {
            Status = dto.Status, 
            Note = dto.Note, 
            ChangedByUserId = dto.PerformedByEmployeeId?.ToString() ?? updatedByUserId, 
            CreatedAt = TimeHelper.GetEgyptTime()
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
                if (!order.Payments.Any(p => p.Method == order.PaymentMethod))
                {
                    order.Payments.Add(new OrderPayment { Method = order.PaymentMethod, Amount = order.TotalAmount, CreatedAt = TimeHelper.GetEgyptTime() });
                }
            }
        }

        if (dto.Status == OrderStatus.Delivered)
        {
            order.ActualDeliveryDate = TimeHelper.GetEgyptTime();
            if (order.PaymentMethod != PaymentMethod.Credit)
            {
                order.PaymentStatus = PaymentStatus.Paid;
                order.PaidAmount = order.TotalAmount; // ✅ تحديث المبلغ المدفوع عند التسليم الفعلي (للكاش والوسائل الأخرى)
                if (!order.Payments.Any(p => p.Method == order.PaymentMethod))
                {
                    order.Payments.Add(new OrderPayment { Method = order.PaymentMethod, Amount = order.TotalAmount, CreatedAt = TimeHelper.GetEgyptTime() });
                }
            }
            
            // ✅ Evaluate customer category after delivery
            await _customerService.EvaluateCustomerCategoryAsync(order.CustomerId);

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

        if (dto.Status == OrderStatus.Returned)
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
                var now = TimeHelper.GetEgyptTime();
                var isSameBusinessDay = returnEntries.All(e => TimeHelper.GetEgyptBusinessDayDate(e.CreatedAt) == TimeHelper.GetEgyptBusinessDayDate(now));

                if (isSameBusinessDay)
                {
                    _db.JournalLines.RemoveRange(returnEntries.SelectMany(e => e.Lines));
                    _db.JournalEntries.RemoveRange(returnEntries);
                    _logger.LogInformation("[Accounting] Voided {Count} SalesReturn entries for order {Ref} on status revert.", returnEntries.Count, order.OrderNumber);
                }
                else
                {
                    var entriesToReverse = returnEntries.Where(e => e.Status != JournalEntryStatus.Reversed).ToList();
                    foreach (var e in entriesToReverse)
                    {
                        await _accounting.ReverseEntryAsync(e.Id, $"إلغاء المرتجع للفاتورة رقم {order.OrderNumber}");
                    }
                    _logger.LogInformation("[Accounting] Reversed {Count} SalesReturn entries for order {Ref} on status revert.", returnEntries.Count, order.OrderNumber);
                }
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
                            order.OrderNumber, "Revert: Order status changed from Returned", updatedByUserId,
                            0, // unitCost fallback
                            order.Source,
                            autoSave: false,
                            ignoreIdempotency: true
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
                    item.Quantity, item.ProductId, item.ProductVariantId, 
                    order.OrderNumber, 
                    $"Order {dto.Status}", updatedByUserId,
                    0, // unitCost fallback
                    order.Source,
                    autoSave: false,
                    ignoreIdempotency: true
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
        // 💳 CANCEL OR RESTORE LINKED INSTALLMENTS ON ORDER STATUS CHANGE
        if (dto.Status == OrderStatus.Cancelled || dto.Status == OrderStatus.Returned)
        {
            var linkedInstallments = await _db.CustomerInstallments
                .Where(i => i.OrderId == orderId && i.Status != InstallmentStatus.Cancelled)
                .ToListAsync();
            foreach (var inst in linkedInstallments)
            {
                inst.Status = InstallmentStatus.Cancelled;
                inst.UpdatedAt = TimeHelper.GetEgyptTime();
            }
        }
        else if ((oldStatus == OrderStatus.Returned || oldStatus == OrderStatus.Cancelled) &&
                 dto.Status != OrderStatus.Returned && dto.Status != OrderStatus.Cancelled)
        {
            var linkedInstallments = await _db.CustomerInstallments
                .Where(i => i.OrderId == orderId && i.Status == InstallmentStatus.Cancelled)
                .ToListAsync();
            foreach (var inst in linkedInstallments)
            {
                inst.Status = inst.DueDate < DateTime.Today ? InstallmentStatus.Overdue : InstallmentStatus.Pending;
                inst.UpdatedAt = TimeHelper.GetEgyptTime();
            }
        }

        await _db.SaveChangesAsync();

        if (dto.Status == OrderStatus.Returned || dto.Status == OrderStatus.Cancelled)
        {
            _ = PostSalesReturnWithRetryAsync(orderId, dto.RefundAccountId);
        }

        return (await GetOrderByIdAsync(orderId))!;
    }

    public async Task<OrderDetailDto> ProcessPartialReturnAsync(int orderId, PartialReturnDto dto, string updatedByUserId)
    {
        var returnedOrderItems = new List<OrderItem>();
        decimal refundAmount = 0;
        string? reference = null;

        var strategy = _db.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var order = await _db.Orders.Include(o => o.Items).ThenInclude(i => i.Product).FirstOrDefaultAsync(o => o.Id == orderId);
                if (order == null) throw new KeyNotFoundException("Order not found.");
                if (order.Status == OrderStatus.Cancelled) throw new InvalidOperationException("Cannot return items from a cancelled order.");

                var ticksSuffix = TimeHelper.GetEgyptTime().Ticks.ToString().Substring(10);
                reference = $"{order.OrderNumber}-PRT-{ticksSuffix}";

                // 1. Calculate Refund Amount (Proportional to Total Paid) First
                decimal refundAmountTotal = 0;
                var itemsTotal = order.Items.Sum(i => i.TotalPrice) > 0 ? order.Items.Sum(i => i.TotalPrice) : 1;

                foreach (var req in dto.Items)
                {
                    var line = order.Items.FirstOrDefault(i => i.Id == req.OrderItemId);
                    if (line != null && req.Quantity > 0)
                    {
                        var lineShare = line.TotalPrice * ((decimal)req.Quantity / line.Quantity);
                        refundAmountTotal += Math.Round(lineShare * (order.TotalAmount / itemsTotal), 2);
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
                    var itemTotalReturn = Math.Round((line.TotalPrice * ((decimal)req.Quantity / line.Quantity)) * (order.TotalAmount / itemsTotal), 2);
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
                        order.OrderNumber, $"Partial Return: {req.Quantity} units", updatedByUserId,
                        0, // unitCost fallback
                        order.Source,
                        autoSave: false,
                        ignoreIdempotency: true
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
                    ChangedByUserId = dto.PerformedByEmployeeId?.ToString() ?? updatedByUserId,
                    CreatedAt = TimeHelper.GetEgyptTime()
                });

                order.UpdatedAt = TimeHelper.GetEgyptTime();

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return (await GetOrderByIdAsync(orderId))!;
            }
            catch { await tx.RollbackAsync(); throw; }
        });

        // 6. Post Accounting
        _ = PostPartialReturnWithRetryAsync(orderId, returnedOrderItems, refundAmount, dto.RefundAccountId, dto.RefundToStoreCredit, reference);

        return result;
    }

    public async Task<string> ProcessDirectReturnAsync(DirectReturnDto dto, string updatedByUserId)
    {
        var returnNumber = await _seq.NextAsync("RTN-POS");
        decimal totalCost = 0;

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var customerLabel = !string.IsNullOrEmpty(dto.CustomerName)
                    ? dto.CustomerName
                    : (dto.CustomerId.HasValue ? $"Customer#{dto.CustomerId}" : "Walk-in");

                foreach (var item in dto.Items)
                {
                    // 1. Fetch product cost for COGS + use UnitPrice from dto
                    var product = await _db.Products.FindAsync(item.ProductId);
                    var unitCostValue = item.UnitPrice > 0 ? item.UnitPrice : (product?.CostPrice ?? 0);

                    if (product != null)
                    {
                        totalCost += (product.CostPrice ?? 0) * item.Quantity;
                    }

                    // 2. Inventory Update — force:true allows return even if stock is 0
                    await _inventory.LogMovementAsync(
                        InventoryMovementType.ReturnIn,
                        item.Quantity, item.ProductId, item.ProductVariantId,
                        returnNumber,
                        $"Direct Return | {dto.Reason ?? "مرتجع مبيعات خارجي"} | Customer: {customerLabel}",
                        updatedByUserId,
                        unitCostValue,
                        OrderSource.POS,
                        autoSave: false,
                        force: true
                    );
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        });

        // Post Accounting
        _ = PostDirectReturnWithRetryAsync(dto, returnNumber, totalCost);

        return returnNumber;
    }

    private async Task PostDirectReturnWithRetryAsync(DirectReturnDto dto, string returnNumber, decimal totalCost)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var accounting = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                await accounting.PostDirectSalesReturnAsync(dto, returnNumber, totalCost);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "[Accounting] PostDirectReturn attempt {Attempt}/{Max} failed for {Number}. Retrying...", attempt, maxAttempts, returnNumber);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Accounting] PostDirectReturn permanently failed for {Number} after {Max} attempts.", returnNumber, maxAttempts);
            }
        }
    }


    public async Task<string> GenerateOrderNumberAsync(OrderSource source = OrderSource.Website)
    {
        var store = await _db.StoreInfo.AsNoTracking().FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        var basePrefix = store?.OrderNumberPrefix ?? "SPT";

        var prefix = source == OrderSource.POS ? "POS" : basePrefix;
        return await _seq.NextAsync(prefix);
    }

    public async Task SyncAllOrderAccountingAsync(int? daysLimit = null)
    {
        var query = _db.Orders.Include(o => o.Customer).Include(o => o.Items).ThenInclude(i => i.Product)
            .Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Pending);

        if (daysLimit.HasValue)
        {
            var limitDate = TimeHelper.GetEgyptTime().AddDays(-daysLimit.Value);
            query = query.Where(o => o.CreatedAt >= limitDate);
        }

        var orders = await query.ToListAsync();

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

                if (entry == null || !entry.Lines.Any())
                {
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

    private async Task PostSalesReturnWithRetryAsync(int orderId, int? refundAccountId = null)
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
                await accounting.PostSalesReturnAsync(order, refundAccountId);
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

    private async Task PostPartialReturnWithRetryAsync(int orderId, List<OrderItem> returnedItems, decimal refundAmount, int? refundAccountId = null, bool refundToStoreCredit = false, string? reference = null)
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
                await accounting.PostPartialSalesReturnAsync(order, returnedItems, refundAmount, refundAccountId, refundToStoreCredit, overrideReference: reference);
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

    public async Task<OrderDetailDto> ConvertToCostAsync(int orderId, string refundMethod, string updatedByUserId)
    {
        decimal originalTotalAmount = 0;
        decimal originalVatAmount = 0;

        var strategy = _db.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var order = await _db.Orders
                    .Include(o => o.Items)
                    .Include(o => o.Payments)
                    .FirstOrDefaultAsync(o => o.Id == orderId);

                if (order == null) throw new KeyNotFoundException("Order not found.");
                if (order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Returned)
                    throw new InvalidOperationException("لا يمكن تحويل الفواتير الملغاة أو المرتجعة بالكامل لسعر التكلفة.");

                // Preload products of items
                var productIds = order.Items.Select(i => i.ProductId).Where(id => id.HasValue).Select(id => id!.Value).ToList();
                var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

                originalTotalAmount = order.TotalAmount;
                originalVatAmount = order.TotalVatAmount;

                decimal newSubTotal = 0;
                foreach (var item in order.Items)
                {
                    if (item.ProductId.HasValue && products.TryGetValue(item.ProductId.Value, out var product))
                    {
                        decimal costPrice = product.CostPrice ?? item.UnitPrice;
                        item.UnitPrice = costPrice;
                    }
                    
                    item.TotalPrice = item.UnitPrice * item.Quantity;
                    if (item.HasTax)
                    {
                        var rate = (item.VatRateApplied ?? 0) / 100m;
                        decimal net = Math.Round(item.TotalPrice / (1 + rate), 2);
                        item.ItemVatAmount = item.TotalPrice - net;
                    }
                    else
                    {
                        item.ItemVatAmount = 0;
                    }
                    newSubTotal += item.TotalPrice;
                }

                decimal newTotalAmount = newSubTotal;
                decimal difference = originalTotalAmount - newTotalAmount;

                if (difference <= 0)
                {
                    throw new InvalidOperationException("الفاتورة بالفعل بسعر التكلفة أو أن سعر التكلفة أعلى من سعر البيع الحالي.");
                }

                // Reset discount fields
                order.DiscountAmount = 0;
                order.TemporalDiscount = 0;
                order.CouponCode = null;

                order.SubTotal = newSubTotal;
                order.TotalVatAmount = order.Items.Sum(i => i.ItemVatAmount);
                order.TotalAmount = newTotalAmount;
                order.PaymentMethod = PaymentMethod.CostPrice;

                // Adjust PaidAmount & Payments
                decimal excessPaid = order.PaidAmount - newTotalAmount;
                if (excessPaid > 0)
                {
                    order.PaidAmount = newTotalAmount;
                    decimal remainingExcess = excessPaid;
                    foreach (var p in order.Payments.OrderByDescending(p => p.Amount))
                    {
                        if (remainingExcess <= 0) break;
                        if (p.Amount >= remainingExcess)
                        {
                            p.Amount -= remainingExcess;
                            remainingExcess = 0;
                        }
                        else
                        {
                            remainingExcess -= p.Amount;
                            p.Amount = 0;
                        }
                    }
                    var zeroPayments = order.Payments.Where(p => p.Amount == 0).ToList();
                    foreach (var zp in zeroPayments)
                    {
                        order.Payments.Remove(zp);
                    }
                }

                order.StatusHistory.Add(new OrderStatusHistory
                {
                    Status = order.Status,
                    Note = $"[تحويل لسعر التكلفة] تم تحويل أسعار الفاتورة لسعر التكلفة وإرجاع الفرق ({difference:N2} جنيه) بطريقة: {(refundMethod == "cash" ? "استرداد نقدي" : "إضافة لرصيد الحساب")}",
                    ChangedByUserId = updatedByUserId,
                    CreatedAt = TimeHelper.GetEgyptTime()
                });

                order.UpdatedAt = TimeHelper.GetEgyptTime();

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return (await GetOrderByIdAsync(orderId))!;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });

        // Post Accounting entry in background
        _ = PostCostPriceAdjustmentWithRetryAsync(orderId, originalTotalAmount, originalVatAmount, refundMethod);

        return result;
    }

    private async Task PostCostPriceAdjustmentWithRetryAsync(int orderId, decimal originalTotalAmount, decimal originalVatAmount, string refundMethod)
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
                await accounting.PostCostPriceAdjustmentAsync(order, originalTotalAmount, originalVatAmount, refundMethod);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "[Accounting] PostCostPriceAdjustment attempt {Attempt}/{Max} failed for order {OrderId}. Retrying...", attempt, maxAttempts, orderId);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Accounting] PostCostPriceAdjustment permanently failed for order {OrderId} after {Max} attempts.", orderId, maxAttempts);
            }
        }
    }

    public async Task UpdateSalesReturnAsync(string reference, UpdateSalesReturnDto dto, string updatedByUserId)
    {
        var trimmedRef = reference?.Trim() ?? string.Empty;
        var journalEntry = await _db.JournalEntries
            .Include(e => e.Lines)
            .Include(e => e.Order)
            .ThenInclude(o => o!.Items)
            .FirstOrDefaultAsync(e => (e.Reference == trimmedRef || e.EntryNumber == trimmedRef) && e.Type == JournalEntryType.SalesReturn);

        if (journalEntry == null)
        {
            throw new KeyNotFoundException("Sales return entry not found.");
        }

        var order = journalEntry.Order;
        var now = TimeHelper.GetEgyptTime();

        var oldMovements = await _db.InventoryMovements
            .Where(m => m.Reference == trimmedRef && m.Type == InventoryMovementType.ReturnIn)
            .ToListAsync();

        if (!oldMovements.Any() && order != null)
        {
            var candidateMovements = await _db.InventoryMovements
                .Where(m => m.Reference == order.OrderNumber && m.Type == InventoryMovementType.ReturnIn)
                .ToListAsync();

            oldMovements = candidateMovements
                .Where(m => Math.Abs((m.CreatedAt - journalEntry.EntryDate).TotalMinutes) < 5)
                .ToList();
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                foreach (var mov in oldMovements)
                {
                    if (mov.ProductId > 0)
                    {
                        await _inventory.LogMovementAsync(
                            InventoryMovementType.Sale,
                            -mov.Quantity,
                            mov.ProductId,
                            mov.ProductVariantId,
                            mov.Reference,
                            $"Revert Return: {reference}",
                            updatedByUserId,
                            0,
                            order?.Source ?? OrderSource.POS,
                            autoSave: false,
                            ignoreIdempotency: true
                        );
                    }
                }

                if (order != null)
                {
                    foreach (var mov in oldMovements)
                    {
                        var line = order.Items.FirstOrDefault(i => i.ProductId == mov.ProductId && i.ProductVariantId == mov.ProductVariantId);
                        if (line != null)
                        {
                            line.ReturnedQuantity = Math.Max(0, line.ReturnedQuantity - mov.Quantity);
                        }
                    }
                }

                _db.InventoryMovements.RemoveRange(oldMovements);
                _db.JournalLines.RemoveRange(journalEntry.Lines);
                _db.JournalEntries.Remove(journalEntry);
                await _db.SaveChangesAsync();

                var newReturnItems = dto.Items.Where(i => i.Quantity > 0).ToList();
                if (newReturnItems.Any())
                {
                    var returnDate = dto.ReturnDate ?? now;

                    if (order != null)
                    {
                        var returnedOrderItems = new List<OrderItem>();
                        decimal refundAmount = 0;
                        var itemsTotal = order.Items.Sum(i => i.TotalPrice) > 0 ? order.Items.Sum(i => i.TotalPrice) : 1;

                        foreach (var req in newReturnItems)
                        {
                            var line = order.Items.FirstOrDefault(i => i.ProductId == req.ProductId && i.ProductVariantId == req.ProductVariantId);
                            if (line == null) continue;

                            int maxCanReturn = line.Quantity - line.ReturnedQuantity;
                            if (req.Quantity > maxCanReturn) 
                               throw new InvalidOperationException($"Cannot return {req.Quantity} for item {line.ProductNameAr}. Already returned: {line.ReturnedQuantity}. Max remaining: {maxCanReturn}.");

                            var itemTotalReturn = Math.Round((line.TotalPrice * ((decimal)req.Quantity / line.Quantity)) * (order.TotalAmount / itemsTotal), 2);
                            var itemVatReturn = Math.Round(line.ItemVatAmount * ((decimal)req.Quantity / line.Quantity), 2);
                            
                            refundAmount += itemTotalReturn;
                            line.ReturnedQuantity += req.Quantity;

                            var returnClone = new OrderItem
                            {
                                ProductId = line.ProductId,
                                ProductVariantId = line.ProductVariantId,
                                ProductNameAr = line.ProductNameAr,
                                Quantity = req.Quantity,
                                UnitPrice = line.UnitPrice,
                                TotalPrice = itemTotalReturn,
                                ItemVatAmount = itemVatReturn,
                                Product = line.Product
                            };
                            returnedOrderItems.Add(returnClone);

                            await _inventory.LogMovementAsync(
                                InventoryMovementType.ReturnIn,
                                req.Quantity, line.ProductId, line.ProductVariantId,
                                trimmedRef, $"Return: {req.Quantity} units (Updated)", updatedByUserId,
                                0,
                                order.Source,
                                autoSave: false,
                                ignoreIdempotency: true
                            );
                        }

                        if (order.Items.All(i => i.Quantity == i.ReturnedQuantity))
                        {
                            order.Status = OrderStatus.Returned;
                            order.PaymentStatus = PaymentStatus.Refunded;
                        }
                        else if (order.Items.Any(i => i.ReturnedQuantity > 0))
                        {
                            order.Status = OrderStatus.PartiallyReturned;
                        }
                        else
                        {
                            order.Status = order.Source == OrderSource.POS ? OrderStatus.Confirmed : OrderStatus.Delivered;
                            order.PaymentStatus = PaymentStatus.Paid;
                        }

                        order.StatusHistory.Add(new OrderStatusHistory {
                            Status = order.Status,
                            Note = $"[تعديل مرتجع] {dto.Reason}: {dto.Note}",
                            ChangedByUserId = updatedByUserId,
                            CreatedAt = returnDate
                        });

                        order.UpdatedAt = returnDate;

                        await _db.SaveChangesAsync();

                        bool refundToStoreCredit = dto.RefundMethod == PaymentMethod.CustomerBalance;
                        await _accounting.PostPartialSalesReturnAsync(
                            order, 
                            returnedOrderItems, 
                            refundAmount, 
                            dto.RefundAccountId, 
                            refundToStoreCredit, 
                            overrideReference: trimmedRef, 
                            overrideDate: returnDate
                        );
                    }
                    else
                    {
                        decimal totalCost = 0;
                        var directItems = new List<DirectReturnItemDto>();

                        foreach (var req in newReturnItems)
                        {
                            var product = await _db.Products.FindAsync(req.ProductId);
                            if (product == null) continue;

                            directItems.Add(new DirectReturnItemDto(
                                req.ProductId,
                                req.ProductVariantId,
                                req.Quantity,
                                req.UnitPrice,
                                product.HasTax,
                                product.VatRate
                            ));

                            await _inventory.LogMovementAsync(
                                InventoryMovementType.ReturnIn,
                                req.Quantity, req.ProductId, req.ProductVariantId,
                                trimmedRef, $"Direct Return: {dto.Reason} (Updated)", updatedByUserId,
                                0,
                                OrderSource.POS,
                                autoSave: false
                            );

                            totalCost += (product.CostPrice ?? 0) * req.Quantity;
                        }

                        await _db.SaveChangesAsync();

                        var directDto = new DirectReturnDto(
                            dto.CustomerId,
                            dto.CustomerName,
                            string.Empty,
                            directItems,
                            dto.RefundMethod ?? PaymentMethod.Cash,
                            dto.RefundAccountId,
                            dto.Reason,
                            dto.Note
                        );

                        await _accounting.PostDirectSalesReturnAsync(directDto, trimmedRef, totalCost, overrideDate: returnDate);
                    }
                }
                else
                {
                    if (order != null)
                    {
                        order.Status = order.Source == OrderSource.POS ? OrderStatus.Confirmed : OrderStatus.Delivered;
                        order.PaymentStatus = PaymentStatus.Paid;
                        order.StatusHistory.Add(new OrderStatusHistory {
                            Status = order.Status,
                            Note = $"[إلغاء المرتجع بالكامل]: {dto.Note}",
                            ChangedByUserId = updatedByUserId,
                            CreatedAt = now
                        });
                        await _db.SaveChangesAsync();
                    }
                }

                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        });
    }
}

