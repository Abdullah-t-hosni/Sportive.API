using Sportive.API.Models;

namespace Sportive.API.DTOs;

// ══════════════════════════════════════════════════════
// SUPPLIER DTOs
// ══════════════════════════════════════════════════════

public record CreateSupplierDto(
    string  Name,                // إلزامي
    string  Phone,               // إلزامي
    string? CompanyName  = null,
    string? TaxNumber    = null,
    string? Email        = null,
    string? Address      = null,
    string? AttachmentUrl = null,
    string? AttachmentPublicId = null
);

public record UpdateSupplierDto(
    string  Name,
    string  Phone,
    string? CompanyName,
    string? TaxNumber,
    string? Email,
    string? Address,
    bool    IsActive = true,
    string? AttachmentUrl = null,
    string? AttachmentPublicId = null,
    int?    MainAccountId = null
);

public record SupplierDto(
    int     Id,
    string  Name,
    string  Phone,
    string? CompanyName,
    string? TaxNumber,
    string? Email,
    string? Address,
    bool    IsActive,
    decimal TotalPurchases,
    decimal TotalPaid,
    decimal Balance,
    int     InvoiceCount,
    string? AttachmentUrl = null,
    string? AttachmentPublicId = null
);

public record SupplierBasicDto(int Id, string Name, string Phone, string? CompanyName);

// ══════════════════════════════════════════════════════
// PURCHASE INVOICE DTOs
// ══════════════════════════════════════════════════════

public record InvoicePaymentDto(
    decimal Amount,
    PaymentMethod_Purchase Method,
    int? CashAccountId,
    string? Notes = null
);

public record CreatePurchaseInvoiceDto(
    int      SupplierId,
    PaymentTerms PaymentTerms,
    DateTime  InvoiceDate,
    decimal  TaxPercent,
    bool     IsTaxInclusive,
    List<CreatePurchaseItemDto> Items,
    List<InvoicePaymentDto>? Payments = null,
    int?      PaymentTermDays = null,
    DateTime? DueDate = null,
    string?  SupplierInvoiceNumber = null,
    string?  Notes = null,
    bool     IsAssetPurchase = false,
    decimal  DiscountAmount = 0,
    string?  AttachmentUrl = null,
    string?  AttachmentPublicId = null,
    int? VendorAccountId = null,
    int? InventoryAccountId = null,
    int? ExpenseAccountId = null,
    int? VatAccountId = null,
    int? CashAccountId = null,
    OrderSource? CostCenter = null,
    decimal DeductAdvanceAmount = 0,
    List<int>? AdvancePaymentIds = null,
    int? WarehouseId = null
);

public record CreatePurchaseItemDto(
    string  Description,
    int?    ProductId,
    int?    ProductVariantId = null,
    int?    FixedAssetCategoryId = null,
    string? AssetName = null,
    string? Unit = null,
    decimal Quantity = 1,
    decimal UnitCost = 0,
    decimal TaxRate = 0,
    bool    IsTaxInclusive = false
);

public record UpdatePurchaseInvoiceDto(
    PaymentTerms PaymentTerms,
    DateTime InvoiceDate,
    decimal  TaxPercent,
    bool     IsTaxInclusive,
    List<CreatePurchaseItemDto> Items,
    DateTime? DueDate = null,
    string?  SupplierInvoiceNumber = null,
    string?  Notes = null,
    bool     IsAssetPurchase = false,
    decimal  DiscountAmount = 0,
    string?  AttachmentUrl = null,
    string?  AttachmentPublicId = null,
    OrderSource? CostCenter = null,
    int? SupplierId = null,
    int? CashAccountId = null,
    int? VendorAccountId = null,
    int? InventoryAccountId = null,
    int? VatAccountId = null,
    List<InvoicePaymentDto>? Payments = null,
    decimal DeductAdvanceAmount = 0,
    List<int>? AdvancePaymentIds = null,
    int? WarehouseId = null
);


public record PurchaseInvoiceSummaryDto(
    int      Id,
    string   InvoiceNumber,
    string?  SupplierInvoiceNumber,
    int      SupplierId,
    string   SupplierName,
    string   PaymentTerms,
    bool     IsAssetPurchase,
    string   Status,
    DateTime InvoiceDate,
    DateTime? DueDate,
    decimal  TotalAmount,
    decimal  PaidAmount,
    decimal  RemainingAmount,
    OrderSource? CostCenter = null,
    string? CostCenterLabel = null,
    int? WarehouseId = null,
    string? WarehouseName = null
);

public record PurchaseInvoiceDetailDto(
    int      Id,
    string   InvoiceNumber,
    string?  SupplierInvoiceNumber,
    SupplierBasicDto Supplier,
    string   PaymentTerms,
    string   Status,
    bool     IsAssetPurchase,
    DateTime InvoiceDate,
    DateTime? DueDate,
    decimal  SubTotal,
    decimal  TaxPercent,
    decimal  TaxAmount,
    bool     IsTaxInclusive,
    decimal  DiscountAmount,
    decimal  TotalAmount,
    decimal  PaidAmount,
    decimal  RemainingAmount,
    string?  Notes,
    List<PurchaseItemDto> Items,
    List<SupplierPaymentSummaryDto> Payments,
    string?  AttachmentUrl = null,
    string?  AttachmentPublicId = null,
    OrderSource? CostCenter = null,
    int?     CashAccountId = null,
    int?     SupplierId = null,
    string?  CostCenterLabel = null,
    List<int>? AdvancePaymentIds = null,
    int?     WarehouseId = null,
    string?  WarehouseName = null
);

public record PurchaseItemDto(
    int     Id,
    string  Description,
    int?    ProductId,
    string? ProductSKU,
    string? ProductName,
    int?    ProductVariantId,
    string? Size,
    string?  Color,
    string? Unit,
    decimal UnitMultiplier,
    decimal Quantity,
    decimal ReturnedQuantity,
    decimal UnitCost,
    decimal TaxRate,
    bool    IsTaxInclusive,
    decimal TotalCost,
    List<ProductVariantDto>? ProductVariants = null,
    int?    FixedAssetCategoryId = null,
    string? FixedAssetCategoryName = null,
    string? AssetName = null,
    int?    CreatedAssetId = null
);

// ══════════════════════════════════════════════════════
// SUPPLIER PAYMENT (VOUCHER) DTOs
// ══════════════════════════════════════════════════════

public record CreateSupplierPaymentDto(
    int      SupplierId,
    int?     PurchaseInvoiceId,
    DateTime PaymentDate,
    decimal  Amount,
    PaymentMethod_Purchase PaymentMethod,
    string   AccountName,   // اسم الحساب (الخزينة / البنك)
    int?     CashAccountId, // ID الحساب المختار
    string?  Notes,         // البيان
    string?  ReferenceNumber,
    string?  AttachmentUrl = null,
    string?  AttachmentPublicId = null,
    OrderSource? CostCenter = null
);

public record SupplierPaymentSummaryDto(
    int      Id,
    string   PaymentNumber,
    string   SupplierName,
    string?  InvoiceNumber,
    DateTime PaymentDate,
    decimal  Amount,
    string   PaymentMethod,
    string   AccountName,
    string?  Notes,
    string?  AttachmentUrl = null,
    string?  AttachmentPublicId = null,
    OrderSource? CostCenter = null,
    string? CostCenterLabel = null,
    int?     SupplierId = null,
    int?     PurchaseInvoiceId = null,
    int?     CashAccountId = null,
    string?  ReferenceNumber = null
);

// ══════════════════════════════════════════════════════
// DASHBOARD
// ══════════════════════════════════════════════════════

public record PurchaseDashboardDto(
    decimal TotalPurchasesThisMonth,
    decimal TotalPaid,
    decimal TotalOutstanding,       // المديونية الكلية
    int     OverdueInvoicesCount,
    int     SuppliersCount,
    List<SupplierBalanceDto> TopSupplierBalances
);

public record SupplierBalanceDto(
    int     SupplierId,
    string  SupplierName,
    decimal Balance
);

public record UpdatePurchaseStatusDto(PurchaseInvoiceStatus Status);

// ══════════════════════════════════════════════════════
// PURCHASE RETURN DTOs
// ══════════════════════════════════════════════════════

public record ReturnPurchaseItemDto(int PurchaseInvoiceItemId, decimal Quantity);

public record ReturnPurchaseInvoiceDto(
    DateTime ReturnDate,
    string? Notes,
    string? ReferenceNumber,
    List<ReturnPurchaseItemDto> Items,
    OrderSource? CostCenter = null,
    int? WarehouseId = null
);

public record CreateStandaloneReturnDto(
    int SupplierId,
    DateTime ReturnDate,
    decimal TaxPercent,
    string? Notes,
    string? ReferenceNumber,
    List<CreateStandaloneReturnItemDto> Items,
    decimal DiscountAmount = 0,
    PaymentTerms PaymentTerms = PaymentTerms.Credit,
    int? CashAccountId = null,
    OrderSource? CostCenter = null,
    int? WarehouseId = null
);

public record CreateStandaloneReturnItemDto(
    string Description,
    int? ProductId,
    int? ProductVariantId = null,
    string? Unit = null,
    decimal Quantity = 1,
    decimal UnitCost = 0,
    int? PurchaseInvoiceItemId = null
);

