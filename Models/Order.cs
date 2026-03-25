namespace Sportive.API.Models;

public enum OrderStatus
{
    Pending = 1,
    Confirmed = 2,
    Processing = 3,
    ReadyForPickup = 4,
    OutForDelivery = 5,
    Delivered = 6,
    Cancelled = 7,
    Returned = 8
}

public enum FulfillmentType
{
    Delivery = 1,
    Pickup = 2
}

public enum PaymentMethod
{
    Cash = 1,
    CreditCard = 2,
    Vodafone = 3,
    InstaPay = 4
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
    POS = 1
}

public class Order : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public FulfillmentType FulfillmentType { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

    public int? DeliveryAddressId { get; set; }
    public Address? DeliveryAddress { get; set; }
    public decimal DeliveryFee { get; set; } = 0;
    public DateTime? EstimatedDeliveryDate { get; set; }
    public DateTime? ActualDeliveryDate { get; set; }
    public string? DeliveryNotes { get; set; }

    public DateTime? PickupScheduledAt { get; set; }
    public DateTime? PickupConfirmedAt { get; set; }

    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; } = 0;
    public string? CouponCode { get; set; }
    public decimal TotalAmount { get; set; }

    public string? CustomerNotes { get; set; }
    public string? AdminNotes { get; set; }

    public string? SalesPersonId { get; set; }
    public OrderSource Source { get; set; } = OrderSource.Website;

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();
}

public class OrderItem : BaseEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public string ProductNameAr { get; set; } = string.Empty;
    public string ProductNameEn { get; set; } = string.Empty;
    public string? Size { get; set; }
    public string? Color { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
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
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }
    public int Quantity { get; set; } = 1;
}
