using Sportive.API.Models;
using System.Collections.Generic;

namespace Sportive.API.DTOs;

public record CreateInventoryAuditDto(
    string Title,
    string? Description,
    List<CreateInventoryAuditItemDto> Items
);

public record CreateInventoryAuditItemDto(
    int? ProductId,
    int? ProductVariantId,
    int ActualQuantity,
    string? Note = null
);

public record UpdateInventoryAuditDto(
    string Title,
    string? Description,
    InventoryAuditStatus Status,
    List<CreateInventoryAuditItemDto> Items
);

public record InventoryAuditSummaryDto(
    int Id,
    string Title,
    DateTime AuditDate,
    int Status,
    decimal TotalExpectedValue,
    decimal TotalActualValue,
    decimal ValueDifference,
    int ItemCount
);

public record InventoryAuditDetailDto(
    int Id,
    string Title,
    DateTime AuditDate,
    string? Description,
    int Status,
    decimal TotalExpectedValue,
    decimal TotalActualValue,
    decimal ValueDifference,
    List<InventoryAuditItemDto> Items,
    int? JournalEntryId = null
);

public record InventoryAuditItemDto(
    int Id,
    int? ProductId,
    string? ProductNameAr,
    string? ProductSKU,
    int? ProductVariantId,
    string? VariantName,
    int ExpectedQuantity,
    int ActualQuantity,
    int Difference,
    decimal UnitCost,
    decimal TotalExpectedValue,
    decimal TotalActualValue,
    string? Note
);
