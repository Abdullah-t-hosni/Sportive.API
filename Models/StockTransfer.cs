using System;
using System.Collections.Generic;

namespace Sportive.API.Models;

public enum StockTransferStatus
{
    Draft = 1,        // مسودة
    Pending = 2,      // بانتظار الشحن/الموافقة
    Shipped = 3,      // تم الشحن (خرجت البضاعة من المخزن الأول)
    Received = 4,     // تم الاستلام (دخلت البضاعة المخزن الثاني)
    Cancelled = 5     // ملغي
}

public class StockTransfer : BaseEntity
{
    public string TransferNumber { get; set; } = string.Empty; // رقم التحويل (مثلا ST-2026-0001)
    
    public int SourceWarehouseId { get; set; }
    public Warehouse SourceWarehouse { get; set; } = null!;

    public int DestinationWarehouseId { get; set; }
    public Warehouse DestinationWarehouse { get; set; } = null!;

    public StockTransferStatus Status { get; set; } = StockTransferStatus.Draft;
    public string? Description { get; set; }
    
    public DateTime? ShippedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }

    public string? CreatedByUserId { get; set; }
    public string? ShippedByUserId { get; set; }
    public string? ReceivedByUserId { get; set; }

    public ICollection<StockTransferItem> Items { get; set; } = new List<StockTransferItem>();
}

public class StockTransferItem : BaseEntity
{
    public int StockTransferId { get; set; }
    public StockTransfer StockTransfer { get; set; } = null!;

    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    public int Quantity { get; set; }
    public string? Note { get; set; }
}
