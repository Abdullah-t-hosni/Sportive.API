using System.Text.Json.Serialization;

namespace Sportive.API.DTOs;

public record PartnerPositionDto(
    [property: JsonPropertyName("customerId")] int? CustomerId,
    [property: JsonPropertyName("customerName")] string? CustomerName,
    [property: JsonPropertyName("customerPhone")] string? CustomerPhone,
    [property: JsonPropertyName("customerBalance")] decimal CustomerBalance,
    [property: JsonPropertyName("supplierId")] int? SupplierId,
    [property: JsonPropertyName("supplierName")] string? SupplierName,
    [property: JsonPropertyName("supplierPhone")] string? SupplierPhone,
    [property: JsonPropertyName("supplierBalance")] decimal SupplierBalance,
    [property: JsonPropertyName("netBalance")] decimal NetBalance,
    [property: JsonPropertyName("netStatus")] string NetStatus, // "CustomerOwes" | "SupplierOwes" | "Balanced"
    [property: JsonPropertyName("maxSettlementAmount")] decimal MaxSettlementAmount,
    [property: JsonPropertyName("canSettle")] bool CanSettle
);

public record CreateSettlementDto(
    [property: JsonPropertyName("customerId")] int CustomerId,
    [property: JsonPropertyName("supplierId")] int SupplierId,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("settlementDate")] DateTime? SettlementDate = null,
    [property: JsonPropertyName("notes")] string? Notes = null,
    [property: JsonPropertyName("reference")] string? Reference = null
);

public record SettlementResultDto(
    [property: JsonPropertyName("journalEntryId")] int JournalEntryId,
    [property: JsonPropertyName("entryNumber")] string EntryNumber,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("customerRemainingBalance")] decimal CustomerRemainingBalance,
    [property: JsonPropertyName("supplierRemainingBalance")] decimal SupplierRemainingBalance,
    [property: JsonPropertyName("netRemainingBalance")] decimal NetRemainingBalance,
    [property: JsonPropertyName("message")] string Message
);

public record UnifiedStatementLineDto(
    [property: JsonPropertyName("date")] DateTime Date,
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("sourceRole")] string SourceRole, // "Customer" | "Supplier" | "Settlement"
    [property: JsonPropertyName("transactionType")] string TransactionType,
    [property: JsonPropertyName("customerDebit")] decimal CustomerDebit,
    [property: JsonPropertyName("customerCredit")] decimal CustomerCredit,
    [property: JsonPropertyName("supplierDebit")] decimal SupplierDebit,
    [property: JsonPropertyName("supplierCredit")] decimal SupplierCredit,
    [property: JsonPropertyName("netImpact")] decimal NetImpact,
    [property: JsonPropertyName("runningCustomerBalance")] decimal RunningCustomerBalance,
    [property: JsonPropertyName("runningSupplierBalance")] decimal RunningSupplierBalance,
    [property: JsonPropertyName("runningNetBalance")] decimal RunningNetBalance
);

public record UnifiedStatementResultDto(
    [property: JsonPropertyName("partnerPosition")] PartnerPositionDto PartnerPosition,
    [property: JsonPropertyName("fromDate")] DateTime FromDate,
    [property: JsonPropertyName("toDate")] DateTime ToDate,
    [property: JsonPropertyName("priorCustomerBalance")] decimal PriorCustomerBalance,
    [property: JsonPropertyName("priorSupplierBalance")] decimal PriorSupplierBalance,
    [property: JsonPropertyName("priorNetBalance")] decimal PriorNetBalance,
    [property: JsonPropertyName("lines")] List<UnifiedStatementLineDto> Lines,
    [property: JsonPropertyName("totalSales")] decimal TotalSales,
    [property: JsonPropertyName("totalPurchases")] decimal TotalPurchases,
    [property: JsonPropertyName("totalCustomerPaid")] decimal TotalCustomerPaid,
    [property: JsonPropertyName("totalSupplierPaid")] decimal TotalSupplierPaid,
    [property: JsonPropertyName("totalSettled")] decimal TotalSettled
);

public record LinkEntityRequest(
    [property: JsonPropertyName("targetId")] int TargetId
);
