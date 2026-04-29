using System;
using System.Collections.Generic;

namespace Sportive.API.Models;

public class InventoryOpeningBalance : BaseEntity
{
    public string Reference { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public decimal TotalValue { get; set; }
    public OrderSource? CostCenter { get; set; } // مركز التكلفة
    
    public ICollection<InventoryOpeningBalanceItem> Items { get; set; } = new List<InventoryOpeningBalanceItem>();
}

public class InventoryOpeningBalanceItem : BaseEntity
{
    public int InventoryOpeningBalanceId { get; set; }
    public InventoryOpeningBalance InventoryOpeningBalance { get; set; } = null!;
    
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    
    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }
    
    public int Quantity { get; set; }
    public decimal CostPrice { get; set; }
    public decimal TotalCost => Quantity * CostPrice;
}
