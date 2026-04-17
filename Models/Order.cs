namespace Sportive.API.Models;

public enum OrderStatus
{
    Pending = 1,        // في الانتظار
    Confirmed = 2,      // مؤكد
    Processing = 3,     // قيد التحضير
    ReadyForPickup = 4, // جاهز للاستلام
    OutForDelivery = 5, // خرج للتوصيل
    Delivered = 6,      // تم التوصيل
    Cancelled = 7,      // ملغي
    Returned = 8,       // مرتجع كامل
    PartiallyReturned = 9 // مرتجع جزئي
}

public enum FulfillmentType
{
    Delivery = 1,   // توصيل للبيت
    Pickup = 2      // استلام من المحل
}

public enum PaymentMethod
{
    Cash       = 1,
    CreditCard = 2,
    Vodafone   = 3,
    InstaPay   = 4,
    Credit     = 5, // آجل / مديونية
    Bank       = 6, // بنك / فيزا
    Mixed      = 7  // جزء وجزء
}

public enum PaymentStatus
{
    Pending = 1,
    Paid = 2,
    Failed = 3,
    Refunded = 4
}

public enum OrderSource
{
    Website = 0,
    POS     = 1,
}

public class Order : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty; // e.g. SZ-20240001
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public FulfillmentType FulfillmentType { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public OrderSource Source { get; set; } = OrderSource.Website;

    // Delivery info
    public int? DeliveryAddressId { get; set; }
    public Address? DeliveryAddress { get; set; }
    public decimal DeliveryFee { get; set; } = 0;
    public decimal ActualDeliveryCost { get; set; } = 0; // What we pay the courier
    public DateTime? EstimatedDeliveryDate { get; set; }
    public DateTime? ActualDeliveryDate { get; set; }
    public string? DeliveryNotes { get; set; }

    // Pickup info
    public DateTime? PickupScheduledAt { get; set; }
    public DateTime? PickupConfirmedAt { get; set; }

    // Pricing
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; } = 0;
    public string? CouponCode { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalVatAmount { get; set; } = 0;

    // Notes
    public string? CustomerNotes { get; set; }
    public string? AdminNotes { get; set; }
    public string? AttachmentUrl { get; set; }
    public string? AttachmentPublicId { get; set; }
    
    // Payment Progress
    public decimal PaidAmount { get; set; } = 0;
    public decimal RemainingAmount => TotalAmount - PaidAmount;
    
    // Target Tracking
    public string? SalesPersonId { get; set; } // The ID of the employee who made the sale using POS

    // Archive
    public bool IsArchived { get; set; } = false;
    public DateTime? ArchivedAt { get; set; }

    // Navigation
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();

    /// <summary>
    /// مدفوعات الطلب المنفصلة — بديل عن تخزين JSON في AdminNotes
    /// يدعم حالات الدفع المتعدد (Mixed Payment) بشكل صحيح
    /// </summary>
    public ICollection<OrderPayment> Payments { get; set; } = new List<OrderPayment>();
}

public class OrderItem : BaseEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public string ProductNameAr { get; set; } = string.Empty; // snapshot at time of order
    public string ProductNameEn { get; set; } = string.Empty;
    public string? Size { get; set; }
    public string? Color { get; set; }
    public int Quantity { get; set; }
    public int ReturnedQuantity { get; set; } = 0; // The amount returned back to stock
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public decimal OriginalUnitPrice { get; set; } // السعر الأصلي قبل أي خصم
    public decimal DiscountAmount { get; set; }     // إجمالي الخصم على هذا السطر (الفرق بين السعر الأصلي والسعر الحالي مضروباً في الكمية)
    public bool HasTax { get; set; } = true;
    public decimal? VatRateApplied { get; set; }
    public decimal ItemVatAmount { get; set; } = 0;
}

public class OrderStatusHistory : BaseEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public OrderStatus Status { get; set; }
    public string? Note { get; set; }
    public string? ChangedByUserId { get; set; }
}

public class CartItem : BaseEntity
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }
    public int Quantity { get; set; } = 1;
}
