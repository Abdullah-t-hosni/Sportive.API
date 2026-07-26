using System;
using System.Collections.Generic;

namespace Sportive.API.DTOs;

public class CreateReturnExchangeRequestDto
{
    public string Type { get; set; } = "Exchange"; // "Exchange" | "Return"
    public List<ReturnExchangeItemInputDto> Items { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
    public string? CustomerNotes { get; set; }
}

public class ReturnExchangeItemInputDto
{
    public int OrderItemId { get; set; }
    public int Quantity { get; set; } = 1;
    public string? ReplacementNote { get; set; }
    public int? ProductId { get; set; }
    public int? ProductVariantId { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
}

public class ConfirmWarehouseReceiptDto
{
    public int? RefundAccountId { get; set; }
    public bool? RefundShipping { get; set; } = false;
    public string? AdminNotes { get; set; }
}

public class RejectReturnExchangeRequestDto
{
    public string? Reason { get; set; }
}

public class ReturnExchangeRequestListFilterDto
{
    public string? Type { get; set; }
    public string? Status { get; set; }
    public string? Search { get; set; }
}

public class ReturnExchangeRequestSummaryDto
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Exchanges { get; set; }
    public int Returns { get; set; }
}

public class ReturnExchangeRequestItemResponseDto
{
    public int Id { get; set; }
    public int OrderItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Size { get; set; }
    public string? Color { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public string? ReplacementNote { get; set; }
}

public class ReturnExchangeRequestResponseDto
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
    public string? CustomerNotes { get; set; }
    public string? AdminNotes { get; set; }
    public string? RejectionReason { get; set; }

    public string ItemSummary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ReceivedAtWarehouseAt { get; set; }

    public List<ReturnExchangeRequestItemResponseDto> Items { get; set; } = new();
}

public class ReturnExchangeRequestsPagedResultDto
{
    public List<ReturnExchangeRequestResponseDto> Items { get; set; } = new();
    public ReturnExchangeRequestSummaryDto Summary { get; set; } = new();
}
