using System;
using System.Collections.Generic;

namespace Sportive.API.Models;

public enum ReturnExchangeType
{
    Exchange = 1,
    Return = 2
}

public enum ReturnExchangeStatus
{
    Pending = 1,              // قيد الانتظار
    Approved = 2,             // موافق عليه تمهيدياً (بانتظار الشحن/وصول القطع للمخزن)
    ReceivedAtWarehouse = 3,  // تم استلام القطع بالمخزن وحساب المرتجع
    Completed = 4,            // مكتمل (للاستبدال)
    Rejected = 5,             // مرفوض
    Cancelled = 6             // ملغى من العميل
}

public class ReturnExchangeRequest : BaseEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public ReturnExchangeType Type { get; set; }
    public ReturnExchangeStatus Status { get; set; } = ReturnExchangeStatus.Pending;

    public string Reason { get; set; } = string.Empty;
    public string? CustomerNotes { get; set; }
    public string? AdminNotes { get; set; }
    public string? RejectionReason { get; set; }

    public int? RefundAccountId { get; set; }
    public bool RefundShipping { get; set; } = false;

    public DateTime? ReceivedAtWarehouseAt { get; set; }

    public ICollection<ReturnExchangeRequestItem> Items { get; set; } = new List<ReturnExchangeRequestItem>();
}

public class ReturnExchangeRequestItem : BaseEntity
{
    public int ReturnExchangeRequestId { get; set; }
    public ReturnExchangeRequest ReturnExchangeRequest { get; set; } = null!;

    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;

    public int Quantity { get; set; }
    public string? ReplacementNote { get; set; } // تفاصيل المقاس أو اللون البديل في حالة الاستبدال
}
