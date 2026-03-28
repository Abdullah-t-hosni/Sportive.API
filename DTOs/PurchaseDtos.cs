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
    bool    IsActive = true
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

public record CreatePurchaseInvoiceDto(
    int      SupplierId,
    PaymentTerms PaymentTerms,
    DateTime InvoiceDate,
    DateTime? DueDate,
    string?  SupplierInvoiceNumber,
    decimal  TaxPercent,
    string?  Notes,
    List<CreatePurchaseItemDto> Items
);

public record CreatePurchaseItemDto(
    string  Description,
    int?    ProductId,
    int?    ProductVariantId = null,
    string? Unit = null,
    int     Quantity = 1,
    decimal UnitCost = 0
);

public record UpdatePurchaseInvoiceDto(
    PaymentTerms PaymentTerms,
    DateTime InvoiceDate,
    DateTime? DueDate,
    string?  SupplierInvoiceNumber,
    decimal  TaxPercent,
    string?  Notes,
    List<CreatePurchaseItemDto> Items
);

public record PurchaseInvoiceSummaryDto(
    int      Id,
    string   InvoiceNumber,
    string?  SupplierInvoiceNumber,
    string   SupplierName,
    string   PaymentTerms,
    string   Status,
    DateTime InvoiceDate,
    DateTime? DueDate,
    decimal  TotalAmount,
    decimal  PaidAmount,
    decimal  RemainingAmount
);

public record PurchaseInvoiceDetailDto(
    int      Id,
    string   InvoiceNumber,
    string?  SupplierInvoiceNumber,
    SupplierBasicDto Supplier,
    string   PaymentTerms,
    string   Status,
    DateTime InvoiceDate,
    DateTime? DueDate,
    decimal  SubTotal,
    decimal  TaxPercent,
    decimal  TaxAmount,
    decimal  TotalAmount,
    decimal  PaidAmount,
    decimal  RemainingAmount,
    string?  Notes,
    List<PurchaseItemDto> Items,
    List<SupplierPaymentSummaryDto> Payments,
    string?  AttachmentUrl = null,
    string?  AttachmentPublicId = null
);

public record PurchaseItemDto(
    int     Id,
    string  Description,
    int?    ProductId,
    string? ProductSKU,
    string? ProductName,
    int?    ProductVariantId,
    string? Size,
    string? Color,
    string? Unit,
    int     Quantity,
    decimal UnitCost,
    decimal TotalCost
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
    string?  Notes,         // البيان
    string?  ReferenceNumber,
    string?  AttachmentUrl = null,
    string?  AttachmentPublicId = null
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
    string?  AttachmentPublicId = null
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
