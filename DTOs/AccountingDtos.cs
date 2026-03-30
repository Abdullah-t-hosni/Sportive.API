using Sportive.API.Models;

namespace Sportive.API.DTOs;

// ══════════════════════════════════════════════════════
// CHART OF ACCOUNTS
// ══════════════════════════════════════════════════════

public record AccountDto(
    int     Id,
    string  Code,
    string  NameAr,
    string? NameEn,
    string? Description,
    string  Type,
    string  Nature,
    int?    ParentId,
    string? ParentName,
    int     Level,
    bool    IsLeaf,
    bool    AllowPosting,
    bool    IsActive,
    bool    IsSystem,
    decimal OpeningBalance,
    decimal CurrentBalance,
    List<AccountDto> Children
);

public record AccountFlatDto(
    int     Id,
    string  Code,
    string  NameAr,
    string? NameEn,
    string  Type,
    string  Nature,
    int?    ParentId,
    int     Level,
    bool    AllowPosting,
    bool    IsActive,
    decimal CurrentBalance
);

public record CreateAccountDto(
    string? Code,
    string  NameAr,
    string? NameEn,
    string? Description,
    AccountType  Type,
    AccountNature Nature,
    int?    ParentId,
    bool    AllowPosting = true
);

public record UpdateAccountDto(
    string  NameAr,
    string? NameEn,
    string? Description,
    bool    AllowPosting,
    bool    IsActive,
    decimal OpeningBalance
);

// ══════════════════════════════════════════════════════
// JOURNAL ENTRIES
// ══════════════════════════════════════════════════════

public record CreateJournalEntryDto(
    DateTime          EntryDate,
    string?           Reference,
    string?           Description,
    List<CreateJournalLineDto> Lines,
    JournalEntryType? Type = JournalEntryType.Manual,
    string?           AttachmentUrl = null,
    string?           AttachmentPublicId = null,
    bool              AsDraft = false
);

public record UpdateJournalEntryDto(
    DateTime          EntryDate,
    string?           Reference,
    string?           Description,
    List<CreateJournalLineDto> Lines,
    string?           AttachmentUrl = null,
    string?           AttachmentPublicId = null,
    bool              PostAfterUpdate = false
);

public record CreateJournalLineDto(
    int     AccountId,
    decimal Debit,
    decimal Credit,
    string? Description,
    int?    CustomerId  = null,
    int?    SupplierId  = null
);

public record JournalEntryDto(
    int      Id,
    string   EntryNumber,
    DateTime EntryDate,
    string   Type,
    string   Status,
    string?  Reference,
    string?  Description,
    decimal  TotalDebit,
    decimal  TotalCredit,
    bool     IsBalanced,
    DateTime CreatedAt,
    List<JournalLineDto> Lines,
    string?  AttachmentUrl = null,
    string?  AttachmentPublicId = null
);

public record JournalLineSummaryDto(
    int      Id,
    string   EntryNumber,
    DateTime EntryDate,
    string?  Description,
    decimal  Debit,
    decimal  Credit,
    decimal  Balance
);

public record JournalLineDto(
    int     Id,
    int     AccountId,
    string  AccountCode,
    string  AccountName,
    decimal Debit,
    decimal Credit,
    string? Description
);

public record JournalEntrySummaryDto(
    int      Id,
    string   EntryNumber,
    DateTime EntryDate,
    string   Type,
    string   Status,
    string?  Reference,
    string?  Description,
    decimal  TotalDebit,
    decimal  TotalCredit,
    string?  AttachmentUrl = null,
    string?  AttachmentPublicId = null
);

// ══════════════════════════════════════════════════════
// RECEIPT VOUCHER
// ══════════════════════════════════════════════════════

    string?  AttachmentPublicId = null
);

public record UpdateReceiptVoucherDto(
    DateTime VoucherDate,
    decimal  Amount,
    int      CashAccountId,
    int      FromAccountId,
    int?     CustomerId,
    VoucherPaymentMethod PaymentMethod,
    string?  Reference,
    string?  Description,
    string?  AttachmentUrl = null,
    string?  AttachmentPublicId = null
);

public record VoucherSummaryDto(
    int      Id,
    string   VoucherNumber,
    DateTime VoucherDate,
    decimal  Amount,
    string   CashAccountName,
    string   CounterpartAccountName,
    string?  EntityName,
    string   PaymentMethod,
    string?  Description,
    string?  AttachmentUrl = null,
    string?  AttachmentPublicId = null
);

// ══════════════════════════════════════════════════════
// PAYMENT VOUCHER
// ══════════════════════════════════════════════════════

public record CreatePaymentVoucherDto(
    DateTime VoucherDate,
    decimal  Amount,
    int      CashAccountId,
    int      ToAccountId,
    int?     SupplierId,
    VoucherPaymentMethod PaymentMethod,
    string?  Reference,
    string?  Description,
    string?  AttachmentUrl = null,
    string?  AttachmentPublicId = null
);

public record UpdatePaymentVoucherDto(
    DateTime VoucherDate,
    decimal  Amount,
    int      CashAccountId,
    int      ToAccountId,
    int?     SupplierId,
    VoucherPaymentMethod PaymentMethod,
    string?  Reference,
    string?  Description,
    string?  AttachmentUrl = null,
    string?  AttachmentPublicId = null
);

// ══════════════════════════════════════════════════════
// ACCOUNT BALANCE (for reports)
// ══════════════════════════════════════════════════════

public record AccountBalanceDto(
    int     AccountId,
    string  Code,
    string  NameAr,
    int     Level,
    decimal Debit,
    decimal Credit,
    decimal Balance,
    string  Nature
);
