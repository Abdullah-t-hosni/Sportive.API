// ============================================================
// DTOs/OrderDtos.cs
// تم الفصل من Dtos.cs الكبير — يشمل Cart, Orders, POS
// ============================================================
using Sportive.API.Models;

namespace Sportive.API.DTOs;

// ========== CART ==========
public record CartItemDto(
    int Id,
    int? ProductId,
    int? ProductVariantId,
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
    string? CustomerName = null,
    string? Note = null,
    decimal? DiscountAmount = null,
    decimal? SubTotal = null,
    List<OrderPaymentDto>? Payments = null,
    string? AttachmentUrl = null,
    string? AttachmentPublicId = null
);

public record OrderPaymentDto(PaymentMethod Method, decimal Amount);

public record CreateOrderItemDto(
    int ProductId,
    int? ProductVariantId,
    int Quantity,
    decimal UnitPrice = 0,
    decimal TotalPrice = 0,
    bool? HasTax = null,
    decimal? VatRate = null
);

// ========== POS ==========
public record CreatePOSOrderDto(
    int? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    List<CreatePOSOrderItemDto> Items,
    decimal TotalAmount,
    PaymentMethod PaymentMethod,
    string? PosEmployeeId,
    OrderSource OrderSource,
    string? Note = null,
    decimal? DiscountAmount = null,
    decimal? Subtotal = null,
    List<OrderPaymentDto>? Payments = null,
    string? CouponCode = null,
    string? AttachmentUrl = null,
    string? AttachmentPublicId = null
);

public record CreatePOSOrderItemDto(
    int ProductId,
    int? ProductVariantId,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    bool? HasTax = null,
    decimal? VatRate = null
);

// ========== ORDER RESPONSES ==========
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
    string Source,
    string PaymentMethod,
    string PaymentStatus,
    int? CustomerId = null,
    string? AdminNotes = null,
    string? CouponCode = null
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
    string? AttachmentPublicId = null,
    string? CouponCode = null
);

public record OrderItemDto(
    int Id,
    string ProductNameAr,
    string ProductNameEn,
    string? ProductImage,
    string? ProductSlug,
    string? Size,
    string? Color,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    decimal OriginalUnitPrice = 0,
    decimal DiscountAmount = 0,
    bool HasTax = true,
    decimal? VatRateApplied = null,
    decimal ItemVatAmount = 0,
    int ReturnedQuantity = 0
);

public record OrderStatusHistoryDto(
    string Status,
    string? Note,
    DateTime CreatedAt
);

public record UpdateOrderStatusDto(OrderStatus Status, string? Note);
public record UpdatePaymentStatusDto(PaymentStatus PaymentStatus, string? Note);
public record UpdateOrderAdminNoteDto(string Note);


public record PartialReturnDto(
    List<ReturnItemRequest> Items,
    string? Reason,
    string? Note,
    int? RefundAccountId = null
);

public record ReturnItemRequest(
    int OrderItemId,
    int Quantity
);
