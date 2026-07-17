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
public record BulkAddToCartDto(List<AddToCartDto> Items);
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
    decimal? TemporalDiscount = null,
    decimal? SubTotal = null,
    List<OrderPaymentDto>? Payments = null,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("paidAmount")]
    decimal? PaidAmount = null,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("attachmentUrl")]
    string? AttachmentUrl = null,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("attachmentPublicId")]
    string? AttachmentPublicId = null,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("branchId")]
    int? BranchId = null,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("warehouseId")]
    int? WarehouseId = null,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("fbp")]
    string? Fbp = null,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("fbc")]
    string? Fbc = null,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("guestAddress")]
    CreateAddressDto? GuestAddress = null
);

public record UpdateOrderDto(
    int? CustomerId,
    List<CreateOrderItemDto> Items,
    decimal DiscountAmount,
    string? AdminNotes = null,
    PaymentMethod? PaymentMethod = null,
    List<OrderPaymentDto>? Payments = null,
    decimal? PaidAmount = null,
    DateTime? CreatedAt = null,
    string? SalesPersonId = null,
    decimal? DeliveryFee = null
);

public record OrderPaymentDto(PaymentMethod Method, decimal Amount);

public record CreateOrderItemDto(
    int ProductId,
    int? ProductVariantId,
    int Quantity,
    decimal UnitPrice = 0,
    decimal TotalPrice = 0,
    bool? HasTax = null,
    decimal? VatRate = null,
    string? Size = null,
    string? Color = null
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
    decimal? TemporalDiscount = null,
    decimal? Subtotal = null,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("payments")]
    List<OrderPaymentDto>? Payments = null,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("paidAmount")]
    decimal? PaidAmount = null,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("couponCode")]
    string? CouponCode = null,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("attachmentUrl")]
    string? AttachmentUrl = null,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("attachmentPublicId")]
    string? AttachmentPublicId = null,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("offlineRef")]
    string? OfflineRef = null,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("branchId")]
    int? BranchId = null,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("warehouseId")]
    int? WarehouseId = null
);

public record CreatePOSOrderItemDto(
    int ProductId,
    int? ProductVariantId,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    bool? HasTax = null,
    decimal? VatRate = null,
    string? Size = null,
    string? Color = null
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
    decimal PaidAmount,
    DateTime CreatedAt,
    int ItemCount,
    string Source,
    string PaymentMethod,
    string PaymentStatus,
    int? CustomerId,
    string? AdminNotes,
    string? CouponCode,
    List<OrderDetailPaymentDto>? Payments,
    decimal? ReturnedAmount,
    string? SalesPersonId,
    decimal? DiscountAmount = 0,
    decimal? TemporalDiscount = 0,
    DateTime? UpdatedAt = null,
    string? TaxAuthorityQrCode = null
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
    decimal TemporalDiscount,
    decimal DeliveryFee,
    decimal TotalAmount,
    string? CustomerNotes,
    string? AdminNotes,
    DateTime CreatedAt,
    List<OrderItemDto> Items,
    List<OrderStatusHistoryDto> StatusHistory,
    List<OrderDetailPaymentDto>? Payments = null,
    string? SalesPersonName = null,
    string? TotalAmountInWords = null,
    decimal PreviousBalance = 0,
    decimal PaidAmount = 0,
    string Source = "Website",
    string? AttachmentUrl = null,
    string? AttachmentPublicId = null,
    string? CouponCode = null,
    string? ShareHash = null,
    string? TaxAuthorityQrCode = null
);

public record OrderDetailPaymentDto(
    string Method,
    decimal Amount,
    string? Reference = null,
    string? Notes = null,
    DateTime? CreatedAt = null
);

public record OrderItemDto(
    int Id,
    int? ProductId,
    int? ProductVariantId,
    string ProductNameAr,
    string ProductNameEn,
    string? SKU,
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
    DateTime CreatedAt,
    string? ChangedByName = null
);

public record UpdateOrderStatusDto(OrderStatus Status, string? Note, int? PerformedByEmployeeId = null, int? RefundAccountId = null);
public record UpdatePaymentStatusDto(PaymentStatus PaymentStatus, string? Note, int? PerformedByEmployeeId = null);
public record UpdateOrderAdminNoteDto(string Note);


public record PartialReturnDto(
    List<ReturnItemRequest> Items,
    string? Reason,
    string? Note,
    int? RefundAccountId = null,
    int? PerformedByEmployeeId = null,
    bool RefundToStoreCredit = false
);

public record ReturnItemRequest(
    int OrderItemId,
    int Quantity
);

public record UpdateOrderDateDto(DateTime CreatedAt);

// ========== DIRECT RETURN (WITHOUT INVOICE) ==========
public record DirectReturnDto(
    int? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    List<DirectReturnItemDto> Items,
    PaymentMethod RefundMethod,
    int? RefundAccountId,
    string? Reason,
    string? Note
);

public record DirectReturnItemDto(
    int ProductId,
    int? ProductVariantId,
    int Quantity,
    decimal UnitPrice,
    bool HasTax,
    decimal? VatRate
);

// ========== UPDATE SALES RETURN ==========
public record UpdateSalesReturnDto(
    List<UpdateSalesReturnItemDto> Items,
    string Reason,
    string Note,
    int? RefundAccountId = null,
    DateTime? ReturnDate = null,
    PaymentMethod? RefundMethod = null,
    int? CustomerId = null,
    string? CustomerName = null
);

public record UpdateSalesReturnItemDto(
    int ProductId,
    int? ProductVariantId,
    int Quantity,
    decimal UnitPrice = 0
);


