using Sportive.API.Models;

namespace Sportive.API.DTOs;

// ══════════════════════════════════════════════════════
// FIXED ASSET CATEGORY DTOs
// ══════════════════════════════════════════════════════

public record CreateFixedAssetCategoryDto(
    string  Name,
    string? Description                    = null,
    int?    AssetAccountId                 = null,
    int?    AccumDepreciationAccountId     = null,
    int?    DepreciationExpenseAccountId   = null
);

public record UpdateFixedAssetCategoryDto(
    string  Name,
    string? Description                    = null,
    bool    IsActive                       = true,
    int?    AssetAccountId                 = null,
    int?    AccumDepreciationAccountId     = null,
    int?    DepreciationExpenseAccountId   = null
);

public record FixedAssetCategoryDto(
    int     Id,
    string  Name,
    string? Description,
    bool    IsActive,
    int?    AssetAccountId,
    string? AssetAccountName,
    int?    AccumDepreciationAccountId,
    string? AccumDepreciationAccountName,
    int?    DepreciationExpenseAccountId,
    string? DepreciationExpenseAccountName,
    int     AssetCount
);

// ══════════════════════════════════════════════════════
// FIXED ASSET DTOs
// ══════════════════════════════════════════════════════

public record CreateFixedAssetDto(
    string            Name,
    int               CategoryId,
    DateTime          PurchaseDate,
    decimal           PurchaseCost,
    DepreciationMethod DepreciationMethod      = DepreciationMethod.StraightLine,
    int               UsefulLifeYears          = 5,
    decimal           SalvageValue             = 0,
    DateTime?         DepreciationStartDate    = null,
    string?           Description              = null,
    string?           Location                 = null,
    string?           SerialNumber             = null,
    string?           Supplier                 = null,
    int?              PurchaseInvoiceId        = null,
    string?           Notes                    = null,
    string?           AttachmentUrl            = null,
    string?           AttachmentPublicId       = null,
    // ربط محاسبي (يلغي افتراضي الفئة)
    int?              AssetAccountId               = null,
    int?              AccumDepreciationAccountId   = null,
    int?              DepreciationExpenseAccountId = null
);

public record UpdateFixedAssetDto(
    string            Name,
    int               CategoryId,
    DateTime          PurchaseDate,
    decimal           PurchaseCost,
    DepreciationMethod DepreciationMethod,
    int               UsefulLifeYears,
    decimal           SalvageValue,
    DateTime?         DepreciationStartDate,
    string?           Description              = null,
    string?           Location                 = null,
    string?           SerialNumber             = null,
    string?           Supplier                 = null,
    int?              PurchaseInvoiceId        = null,
    string?           Notes                    = null,
    string?           AttachmentUrl            = null,
    string?           AttachmentPublicId       = null,
    int?              AssetAccountId               = null,
    int?              AccumDepreciationAccountId   = null,
    int?              DepreciationExpenseAccountId = null
);

public record FixedAssetDto(
    int               Id,
    string            AssetNumber,
    string            Name,
    string?           Description,
    int               CategoryId,
    string            CategoryName,
    DateTime          PurchaseDate,
    decimal           PurchaseCost,
    decimal           SalvageValue,
    DepreciationMethod DepreciationMethod,
    int               UsefulLifeYears,
    DateTime?         DepreciationStartDate,
    decimal           AccumulatedDepreciation,
    decimal           BookValue,
    AssetStatus       Status,
    string?           Location,
    string?           SerialNumber,
    string?           Supplier,
    int?              PurchaseInvoiceId,
    string?           Notes,
    string?           AttachmentUrl,
    string?           AttachmentPublicId,
    int?              AssetAccountId,
    int?              AccumDepreciationAccountId,
    int?              DepreciationExpenseAccountId,
    DateTime          CreatedAt
);

public record FixedAssetDetailDto(
    FixedAssetDto                  Asset,
    IEnumerable<AssetDepreciationDto> Depreciations,
    AssetDisposalDto?              Disposal
);

// ══════════════════════════════════════════════════════
// DEPRECIATION DTOs
// ══════════════════════════════════════════════════════

public record PostDepreciationDto(
    int       FixedAssetId,
    DateTime  DepreciationDate,
    int       PeriodYear,
    int       PeriodMonth,
    decimal?  OverrideAmount = null,  // لو فيه مبلغ مخصص، وإلا يُحسب تلقائياً
    string?   Notes          = null
);

public record AssetDepreciationDto(
    int      Id,
    string   DepreciationNumber,
    int      FixedAssetId,
    string   AssetName,
    DateTime DepreciationDate,
    int      PeriodYear,
    int      PeriodMonth,
    decimal  DepreciationAmount,
    decimal  AccumulatedBefore,
    decimal  AccumulatedAfter,
    decimal  BookValueAfter,
    string?  Notes,
    int?     JournalEntryId,
    DateTime CreatedAt
);

// ══════════════════════════════════════════════════════
// DISPOSAL DTOs
// ══════════════════════════════════════════════════════

public record PostDisposalDto(
    int          FixedAssetId,
    DisposalType DisposalType,
    DateTime     DisposalDate,
    decimal      SaleProceeds           = 0,
    int?         ProceedsAccountId      = null,  // حساب المتحصلات (خزينة / بنك)
    int?         GainAccountId          = null,
    int?         LossAccountId          = null,
    string?      Buyer                  = null,
    string?      Notes                  = null,
    string?      AttachmentUrl          = null,
    string?      AttachmentPublicId     = null
);

public record AssetDisposalDto(
    int          Id,
    string       DisposalNumber,
    int          FixedAssetId,
    string       AssetName,
    DisposalType DisposalType,
    DateTime     DisposalDate,
    decimal      BookValueAtDisposal,
    decimal      AccumulatedAtDisposal,
    decimal      SaleProceeds,
    decimal      GainLossOnDisposal,
    string?      Buyer,
    string?      Notes,
    string?      AttachmentUrl,
    int?         JournalEntryId,
    DateTime     CreatedAt
);
