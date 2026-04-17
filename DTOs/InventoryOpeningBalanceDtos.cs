using System;
using System.Collections.Generic;

namespace Sportive.API.DTOs;

public record CreateOpeningBalanceDto(
    DateTime Date,
    string? Notes,
    List<CreateOpeningBalanceItemDto> Items,
    bool UpdateProductCost = false
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
    int ItemsCount
);

public record OpeningBalanceDetailDto(
    int Id,
    string Reference,
    DateTime Date,
    string? Notes,
    decimal TotalValue,
    List<OpeningBalanceItemDto> Items
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
