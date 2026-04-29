using System;
using System.Collections.Generic;
using Sportive.API.Models;

namespace Sportive.API.DTOs;

public record CreateOpeningBalanceDto(
    DateTime Date,
    string? Notes,
    List<CreateOpeningBalanceItemDto> Items,
    bool UpdateProductCost = false,
    OrderSource? CostCenter = null
);

public record CreateOpeningBalanceItemDto(
    int? ProductId,
    int? ProductVariantId,
    int Quantity,
    decimal CostPrice
);

public record OpeningBalanceSummaryDto(
    int Id,
    string Reference,
    DateTime Date,
    decimal TotalValue,
    int ItemsCount,
    OrderSource? CostCenter = null
);

public record OpeningBalanceDetailDto(
    int Id,
    string Reference,
    DateTime Date,
    string? Notes,
    decimal TotalValue,
    List<OpeningBalanceItemDto> Items,
    OrderSource? CostCenter = null
);

public record OpeningBalanceItemDto(
    int Id,
    int? ProductId,
    string? ProductName,
    string? SKU,
    int? ProductVariantId,
    string? Size,
    string? Color,
    int Quantity,
    decimal CostPrice,
    decimal TotalCost
);
