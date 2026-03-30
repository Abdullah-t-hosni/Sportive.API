using Sportive.API.Models;

namespace Sportive.API.DTOs;

// ========== AUTH ==========
public record RegisterDto(
    string FirstName,
    string LastName,
    string? Email,
    string Password,
    string? Phone
);

public record LoginDto(string Identifier, string Password);

public record AuthResponseDto(
    string UserId,
    string Token,
    string RefreshToken,
    string Email,
    string FullName,
    IList<string> Roles,
    DateTime ExpiresAt,
    int? CustomerId = null,
    string? Phone = null,
    List<AddressDto>? Addresses = null,
    List<ModulePermissionDto>? ModulePermissions = null
);

public record ModulePermissionDto(string ModuleKey, bool CanView, bool CanEdit);

public record ChangePasswordDto(string CurrentPassword, string NewPassword);
public record ForgotPasswordDto(string Identifier);
public record ResetPasswordDto(string Identifier, string Code, string NewPassword);

// ========== CATEGORY ==========
public record CategoryDto(
    int Id,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string Type,
    string? ImageUrl,
    bool IsActive,
    int ProductCount,
    DateTime CreatedAt,
    int? ParentId = null,
    string? ParentNameAr = null,
    string? ParentNameEn = null,
    List<CategoryDto>? Children = null
);

public record CreateCategoryDto(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    CategoryType Type,
    string? ImageUrl,
    int? ParentId = null
);

public record CreateBrandDto(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? ImageUrl,
    int? ParentId = null
);

public record UpdateBrandDto(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? ImageUrl,
    int? ParentId = null
);

// ========== PRODUCT ==========
public record ProductSummaryDto(
    int Id,
    string NameAr,
    string NameEn,
    decimal Price,
    decimal DiscountPrice,
    string? MainImageUrl,
    string CategoryNameAr,
    string CategoryNameEn,
    string? Brand,
    string Status,
    double AverageRating,
    int ReviewCount,
    int TotalStock,
    int ReorderLevel,
    string SKU,
    bool HasTax,
    DateTime CreatedAt
);

public record ProductDetailDto(
    int Id,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    decimal Price,
    decimal DiscountPrice,
    decimal? CostPrice,
    string SKU,
    string? Brand,
    string Status,
    bool IsFeatured,
    int CategoryId,
    string CategoryNameAr,
    string CategoryNameEn,
    List<ProductVariantDto> Variants,
    List<ProductImageDto> Images,
    double AverageRating,
    int ReviewCount,
    int TotalStock,
    int ReorderLevel,
    bool HasTax,
    DateTime CreatedAt
);

public record ProductVariantDto(
    int Id,
    string? Size,
    string? Color,
    string? ColorAr,
    int StockQuantity,
    int ReorderLevel,
    decimal PriceAdjustment,
    string? ImageUrl,
    string? ImagePublicId = null
);

public record ProductImageDto(int Id, string ImageUrl, string? ImagePublicId = null, bool IsMain = false, int SortOrder = 0, string? ColorAr = null);

public record CreateProductDto(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    decimal Price,
    decimal? DiscountPrice,
    decimal? CostPrice,
    string SKU,
    string? Brand,
    int CategoryId,
    bool IsFeatured,
    List<CreateVariantDto>? Variants,
    int ReorderLevel = 0,
    bool HasTax = true
);

public record UpdateProductDto(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    decimal Price,
    decimal? DiscountPrice,
    decimal? CostPrice,
    string? Brand,
    string SKU,
    int CategoryId,
    bool IsFeatured,
    int ReorderLevel,
    ProductStatus Status,
    bool HasTax = true
);

public record CreateVariantDto(
    string? Size,
    string? Color,
    string? ColorAr,
    int StockQuantity,
    decimal? PriceAdjustment,
    int ReorderLevel = 0
);

// ========== CART ==========
public record CartItemDto(
    int Id,
    int ProductId,
    string ProductNameAr,
    string ProductNameEn,
    string? MainImageUrl,
    string? Size,
    string? Color,
    string? ColorAr,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice
);

public record AddToCartDto(int ProductId, int? ProductVariantId, int Quantity);
public record UpdateCartItemDto(int Quantity);

public record CartSummaryDto(
    List<CartItemDto> Items,
    decimal SubTotal,
    decimal DeliveryFee,
    decimal Total,
    int ItemCount
);

// ========== ORDER ==========
public record CreateOrderDto(
    FulfillmentType FulfillmentType,
    PaymentMethod PaymentMethod,
    int? DeliveryAddressId,
    DateTime? PickupScheduledAt,
    string? CustomerNotes,
    string? CouponCode,
    string? SalesPersonId,
    OrderSource Source,
    List<CreateOrderItemDto>? Items = null,
    string? CustomerPhone = null,
    string? CustomerName = null
);

public record CreateOrderItemDto(
    int ProductId,
    int? ProductVariantId,
    int Quantity
);

public record CreatePOSOrderDto(
    int? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    List<CreatePOSOrderItemDto> Items,
    decimal TotalAmount,
    PaymentMethod PaymentMethod,
    string? PosEmployeeId,
    OrderSource OrderSource
);

public record CreatePOSOrderItemDto(
    int ProductId,
    int? ProductVariantId,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice
);

public record OrderSummaryDto(
    int Id,
    string OrderNumber,
    string CustomerName,
    string CustomerPhone,
    string Status,
    string FulfillmentType,
    decimal TotalAmount,
    DateTime CreatedAt,
    int ItemCount,
    string Source
);

public record OrderDetailDto(
    int Id,
    string OrderNumber,
    CustomerBasicDto Customer,
    string Status,
    string FulfillmentType,
    string PaymentMethod,
    string PaymentStatus,
    AddressDto? DeliveryAddress,
    DateTime? PickupScheduledAt,
    decimal SubTotal,
    decimal DiscountAmount,
    decimal DeliveryFee,
    decimal TotalAmount,
    string? CustomerNotes,
    string? AdminNotes,
    DateTime CreatedAt,
    List<OrderItemDto> Items,
    List<OrderStatusHistoryDto> StatusHistory,
    string? SalesPersonName = null,
    string? TotalAmountInWords = null,
    decimal PreviousBalance = 0,
    decimal PaidAmount = 0,
    string Source = "Website",
    string? AttachmentUrl = null,
    string? AttachmentPublicId = null
);

public record OrderItemDto(
    int Id,
    string ProductNameAr,
    string ProductNameEn,
    string? ProductImage,
    string? Size,
    string? Color,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice
);

public record OrderStatusHistoryDto(
    string Status,
    string? Note,
    DateTime CreatedAt
);

public record UpdateOrderStatusDto(OrderStatus Status, string? Note);

// تغيير حالة الدفع
public record UpdatePaymentStatusDto(PaymentStatus PaymentStatus, string? Note);

// ========== CUSTOMER ==========
public record CustomerBasicDto(
    int Id,
    string FullName,
    string Email,
    string? Phone
);

public record CustomerDetailDto(
    int Id,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    int TotalOrders,
    decimal TotalSpent,
    DateTime CreatedAt,
    List<AddressDto> Addresses,
    string? AppUserId = null
);

public record AddressDto(
    int Id,
    string TitleAr,
    string TitleEn,
    string Street,
    string City,
    string? District,
    string? BuildingNo,
    string? Floor,
    string? ApartmentNo,
    bool IsDefault,
    double? Latitude = null,
    double? Longitude = null
);

public record CreateAddressDto(
    string TitleAr,
    string TitleEn,
    string Street,
    string City,
    string? District,
    string? BuildingNo,
    string? Floor,
    string? ApartmentNo,
    string? AdditionalInfo,
    double? Latitude,
    double? Longitude
);

public record CreateCustomerDto(
    string FirstName,
    string? LastName = null,
    string? Email = null,
    string? Phone = null
);

// ========== DASHBOARD ==========
public record DashboardStatsDto(
    decimal TodaySales,
    decimal TodaySalesGrowth, // % vs yesterday
    decimal ThisMonthSales,
    decimal ThisMonthSalesGrowth, // % vs last month
    decimal TotalRevenue,
    int TotalOrders,
    int TotalOrdersGrowth, // % vs last period
    int PendingOrders,
    int TodayOrders,
    int TotalCustomers,
    int TotalCustomersGrowth, // % vs last month
    int TotalProducts,
    int LowStockProducts,
    int OutOfStockProducts
);

public record AnalyticsSummaryDto(
    List<CategorySalesDto> CategorySales,
    List<TopProductDto> TopSellingProducts,
    List<DailyMetricDto> DailySales,
    decimal AverageOrderValue,
    decimal CustomerRetentionRate
);

public record CategorySalesDto(int CategoryId, string NameAr, string NameEn, int TotalSold, decimal TotalRevenue);
public record DailyMetricDto(DateTime Date, decimal Revenue, int Orders, int NewCustomers);

public record SalesChartDto(string Label, decimal Amount, int OrderCount);

public record TopProductDto(
    int ProductId,
    string NameAr,
    string NameEn,
    string? ImageUrl,
    int TotalSold,
    decimal TotalRevenue
);

public record OrderStatusStatsDto(string Status, int Count, decimal Percentage);

public record AdvancedDashboardStatsDto(
    List<LocationStatDto> SalesByCity,
    List<VipCustomerDto> TopCustomers,
    List<InventoryIntelligenceDto> InventoryInsights,
    AbandonedCartDto AbandonedCarts,
    List<PaymentMethodStatDto> PaymentMethods,
    List<AdminActivityDto> RecentActivity,
    List<StaffPerformanceDto> StaffPerformance
);

public record StaffPerformanceDto(string StaffId, string StaffName, int OrderCount, decimal TotalSales);

public record LocationStatDto(string Name, int OrderCount, decimal TotalRevenue);
public record VipCustomerDto(int Id, string Name, string Email, decimal TotalSpent, int OrderCount);
public record InventoryIntelligenceDto(int ProductId, string Name, int Stock, double AvgDailySales, int? DaysRemaining);
public record AbandonedCartDto(int Count, int TotalItems, decimal PotentialRevenue);
public record PaymentMethodStatDto(string Method, int Count, decimal Revenue);
public record AdminActivityDto(string AdminName, string Action, string Target, DateTime Date);

// ========== PAGINATION ==========
public record PaginatedResult<T>(
    List<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);

public class ProductFilterDto
{
    public int? CategoryId { get; set; }
    public string? Search { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? Brand { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public bool? IsFeatured { get; set; }
    public ProductStatus? Status { get; set; }
    public string SortBy { get; set; } = "createdAt";
    public string SortDir { get; set; } = "desc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;
}

// ========== REVIEWS ==========
public record AddReviewDto(int ProductId, int Rating, string? Comment);

// ========== WISHLIST ==========
public record AddToWishlistDto(int ProductId);

// ========== NOTIFICATIONS ==========
public record SendNotificationDto(
    string TitleAr,
    string TitleEn,
    string MessageAr,
    string MessageEn,
    string Type,
    string? ActionUrl = null,
    int? CustomerId = null  // null = send to all
);
