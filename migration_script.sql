CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

START TRANSACTION;
ALTER DATABASE CHARACTER SET utf8mb4;

CREATE TABLE `Accounts` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Code` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `NameAr` longtext CHARACTER SET utf8mb4 NOT NULL,
    `NameEn` longtext CHARACTER SET utf8mb4 NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `Type` int NOT NULL,
    `Nature` int NOT NULL,
    `ParentId` int NULL,
    `Level` int NOT NULL,
    `IsLeaf` tinyint(1) NOT NULL,
    `AllowPosting` tinyint(1) NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `IsSystem` tinyint(1) NOT NULL,
    `OpeningBalance` decimal(18,2) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_Accounts` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Accounts_Accounts_ParentId` FOREIGN KEY (`ParentId`) REFERENCES `Accounts` (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `AspNetRoles` (
    `Id` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `Name` varchar(256) CHARACTER SET utf8mb4 NULL,
    `NormalizedName` varchar(256) CHARACTER SET utf8mb4 NULL,
    `ConcurrencyStamp` longtext CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_AspNetRoles` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `AspNetUsers` (
    `Id` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `FirstName` longtext CHARACTER SET utf8mb4 NOT NULL,
    `LastName` longtext CHARACTER SET utf8mb4 NOT NULL,
    `ProfileImageUrl` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `UserName` varchar(256) CHARACTER SET utf8mb4 NULL,
    `NormalizedUserName` varchar(256) CHARACTER SET utf8mb4 NULL,
    `Email` varchar(256) CHARACTER SET utf8mb4 NULL,
    `NormalizedEmail` varchar(256) CHARACTER SET utf8mb4 NULL,
    `EmailConfirmed` tinyint(1) NOT NULL,
    `PasswordHash` longtext CHARACTER SET utf8mb4 NULL,
    `SecurityStamp` longtext CHARACTER SET utf8mb4 NULL,
    `ConcurrencyStamp` longtext CHARACTER SET utf8mb4 NULL,
    `PhoneNumber` varchar(255) CHARACTER SET utf8mb4 NULL,
    `PhoneNumberConfirmed` tinyint(1) NOT NULL,
    `TwoFactorEnabled` tinyint(1) NOT NULL,
    `LockoutEnd` datetime(6) NULL,
    `LockoutEnabled` tinyint(1) NOT NULL,
    `AccessFailedCount` int NOT NULL,
    CONSTRAINT `PK_AspNetUsers` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `BackupRecords` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `FileName` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `FilePath` varchar(500) CHARACTER SET utf8mb4 NULL,
    `FileSizeBytes` bigint NOT NULL,
    `DurationMs` bigint NOT NULL,
    `Success` tinyint(1) NOT NULL,
    `EmailSent` tinyint(1) NOT NULL,
    `EmailError` longtext CHARACTER SET utf8mb4 NULL,
    `Error` longtext CHARACTER SET utf8mb4 NULL,
    `TriggerType` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `TriggeredBy` varchar(50) CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_BackupRecords` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `Categories` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `NameAr` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `NameEn` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `DescriptionAr` longtext CHARACTER SET utf8mb4 NULL,
    `DescriptionEn` longtext CHARACTER SET utf8mb4 NULL,
    `Type` int NOT NULL,
    `ImageUrl` longtext CHARACTER SET utf8mb4 NULL,
    `IsActive` tinyint(1) NOT NULL,
    `ParentId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_Categories` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Categories_Categories_ParentId` FOREIGN KEY (`ParentId`) REFERENCES `Categories` (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `Coupons` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Code` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
    `DescriptionAr` longtext CHARACTER SET utf8mb4 NULL,
    `DescriptionEn` longtext CHARACTER SET utf8mb4 NULL,
    `DiscountType` int NOT NULL,
    `DiscountValue` decimal(18,2) NOT NULL,
    `MinOrderAmount` decimal(18,2) NULL,
    `MaxDiscountAmount` decimal(18,2) NULL,
    `MaxUsageCount` int NULL,
    `CurrentUsageCount` int NOT NULL,
    `ExpiresAt` datetime(6) NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_Coupons` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `JournalEntries` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EntryNumber` longtext CHARACTER SET utf8mb4 NOT NULL,
    `EntryDate` datetime(6) NOT NULL,
    `Type` int NOT NULL,
    `Status` int NOT NULL,
    `Reference` longtext CHARACTER SET utf8mb4 NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `ReversalOfId` int NULL,
    `AttachmentUrl` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentPublicId` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_JournalEntries` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_JournalEntries_JournalEntries_ReversalOfId` FOREIGN KEY (`ReversalOfId`) REFERENCES `JournalEntries` (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `Notifications` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `TitleAr` longtext CHARACTER SET utf8mb4 NOT NULL,
    `TitleEn` longtext CHARACTER SET utf8mb4 NOT NULL,
    `MessageAr` longtext CHARACTER SET utf8mb4 NOT NULL,
    `MessageEn` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Type` longtext CHARACTER SET utf8mb4 NOT NULL,
    `IsRead` tinyint(1) NOT NULL,
    `OrderId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_Notifications` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `StoreSettings` (
    `StoreConfigId` int NOT NULL,
    `StoreBrandName` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `StoreSlogan` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `StorePhoneNo` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `StoreWhatsAppNo` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `StoreEmailAddr` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `StorePhysicalAddr` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
    `VatRatePercent` decimal(18,2) NOT NULL,
    `FixedDeliveryFee` decimal(18,2) NOT NULL,
    `FreeDeliveryAt` decimal(18,2) NOT NULL,
    `FacebookPage` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `InstagramPage` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `TikTokPage` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `InMaintenance` tinyint(1) NOT NULL,
    `LastUpdateDate` datetime(6) NOT NULL,
    CONSTRAINT `PK_StoreSettings` PRIMARY KEY (`StoreConfigId`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `AccountSystemMappings` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Key` varchar(120) CHARACTER SET utf8mb4 NOT NULL,
    `AccountId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_AccountSystemMappings` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_AccountSystemMappings_Accounts_AccountId` FOREIGN KEY (`AccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4;

CREATE TABLE `Suppliers` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Phone` longtext CHARACTER SET utf8mb4 NOT NULL,
    `CompanyName` longtext CHARACTER SET utf8mb4 NULL,
    `TaxNumber` longtext CHARACTER SET utf8mb4 NULL,
    `Email` longtext CHARACTER SET utf8mb4 NULL,
    `Address` longtext CHARACTER SET utf8mb4 NULL,
    `IsActive` tinyint(1) NOT NULL,
    `TotalPurchases` decimal(18,2) NOT NULL,
    `TotalPaid` decimal(18,2) NOT NULL,
    `AttachmentUrl` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentPublicId` longtext CHARACTER SET utf8mb4 NULL,
    `MainAccountId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_Suppliers` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Suppliers_Accounts_MainAccountId` FOREIGN KEY (`MainAccountId`) REFERENCES `Accounts` (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `AspNetRoleClaims` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RoleId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `ClaimType` longtext CHARACTER SET utf8mb4 NULL,
    `ClaimValue` longtext CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_AspNetRoleClaims` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_AspNetRoleClaims_AspNetRoles_RoleId` FOREIGN KEY (`RoleId`) REFERENCES `AspNetRoles` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `AspNetUserClaims` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `ClaimType` longtext CHARACTER SET utf8mb4 NULL,
    `ClaimValue` longtext CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_AspNetUserClaims` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_AspNetUserClaims_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `AspNetUserLogins` (
    `LoginProvider` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `ProviderKey` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `ProviderDisplayName` longtext CHARACTER SET utf8mb4 NULL,
    `UserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK_AspNetUserLogins` PRIMARY KEY (`LoginProvider`, `ProviderKey`),
    CONSTRAINT `FK_AspNetUserLogins_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `AspNetUserRoles` (
    `UserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `RoleId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK_AspNetUserRoles` PRIMARY KEY (`UserId`, `RoleId`),
    CONSTRAINT `FK_AspNetUserRoles_AspNetRoles_RoleId` FOREIGN KEY (`RoleId`) REFERENCES `AspNetRoles` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_AspNetUserRoles_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `AspNetUserTokens` (
    `UserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `LoginProvider` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `Name` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `Value` longtext CHARACTER SET utf8mb4 NULL,
    CONSTRAINT `PK_AspNetUserTokens` PRIMARY KEY (`UserId`, `LoginProvider`, `Name`),
    CONSTRAINT `FK_AspNetUserTokens_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `Customers` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `FirstName` longtext CHARACTER SET utf8mb4 NOT NULL,
    `LastName` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Email` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `Phone` varchar(255) CHARACTER SET utf8mb4 NULL,
    `AppUserId` varchar(255) CHARACTER SET utf8mb4 NULL,
    `DateOfBirth` datetime(6) NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_Customers` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Customers_AspNetUsers_AppUserId` FOREIGN KEY (`AppUserId`) REFERENCES `AspNetUsers` (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `UserModulePermissions` (
    `PermissionEntryId` int NOT NULL AUTO_INCREMENT,
    `UserAccountID` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `ModuleKey` longtext CHARACTER SET utf8mb4 NOT NULL,
    `CanView` tinyint(1) NOT NULL,
    `CanEdit` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_UserModulePermissions` PRIMARY KEY (`PermissionEntryId`),
    CONSTRAINT `FK_UserModulePermissions_AspNetUsers_UserAccountID` FOREIGN KEY (`UserAccountID`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `Products` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `NameAr` longtext CHARACTER SET utf8mb4 NOT NULL,
    `NameEn` longtext CHARACTER SET utf8mb4 NOT NULL,
    `DescriptionAr` longtext CHARACTER SET utf8mb4 NULL,
    `DescriptionEn` longtext CHARACTER SET utf8mb4 NULL,
    `Price` decimal(18,2) NOT NULL,
    `DiscountPrice` decimal(18,2) NULL,
    `CostPrice` decimal(65,30) NULL,
    `SKU` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
    `Brand` longtext CHARACTER SET utf8mb4 NULL,
    `Status` int NOT NULL,
    `IsFeatured` tinyint(1) NOT NULL,
    `TotalStock` int NOT NULL,
    `ReorderLevel` int NOT NULL,
    `CategoryId` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_Products` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Products_Categories_CategoryId` FOREIGN KEY (`CategoryId`) REFERENCES `Categories` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE TABLE `JournalLines` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `JournalEntryId` int NOT NULL,
    `AccountId` int NOT NULL,
    `Debit` decimal(18,2) NOT NULL,
    `Credit` decimal(18,2) NOT NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `CustomerId` int NULL,
    `SupplierId` int NULL,
    `OrderId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_JournalLines` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_JournalLines_Accounts_AccountId` FOREIGN KEY (`AccountId`) REFERENCES `Accounts` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_JournalLines_JournalEntries_JournalEntryId` FOREIGN KEY (`JournalEntryId`) REFERENCES `JournalEntries` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `PaymentVouchers` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `VoucherNumber` longtext CHARACTER SET utf8mb4 NOT NULL,
    `VoucherDate` datetime(6) NOT NULL,
    `Amount` decimal(18,2) NOT NULL,
    `CashAccountId` int NOT NULL,
    `ToAccountId` int NOT NULL,
    `SupplierId` int NULL,
    `PaymentMethod` int NOT NULL,
    `Reference` longtext CHARACTER SET utf8mb4 NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `JournalEntryId` int NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentUrl` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentPublicId` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_PaymentVouchers` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_PaymentVouchers_Accounts_CashAccountId` FOREIGN KEY (`CashAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_PaymentVouchers_Accounts_ToAccountId` FOREIGN KEY (`ToAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_PaymentVouchers_JournalEntries_JournalEntryId` FOREIGN KEY (`JournalEntryId`) REFERENCES `JournalEntries` (`Id`),
    CONSTRAINT `FK_PaymentVouchers_Suppliers_SupplierId` FOREIGN KEY (`SupplierId`) REFERENCES `Suppliers` (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `PurchaseInvoices` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `InvoiceNumber` longtext CHARACTER SET utf8mb4 NOT NULL,
    `SupplierInvoiceNumber` longtext CHARACTER SET utf8mb4 NULL,
    `SupplierId` int NOT NULL,
    `PaymentTerms` int NOT NULL,
    `Status` int NOT NULL,
    `InvoiceDate` datetime(6) NOT NULL,
    `DueDate` datetime(6) NULL,
    `SubTotal` decimal(18,2) NOT NULL,
    `TaxPercent` decimal(5,2) NOT NULL,
    `TaxAmount` decimal(18,2) NOT NULL,
    `DiscountAmount` decimal(65,30) NOT NULL,
    `TotalAmount` decimal(18,2) NOT NULL,
    `PaidAmount` decimal(18,2) NOT NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentUrl` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentPublicId` longtext CHARACTER SET utf8mb4 NULL,
    `VendorAccountId` int NULL,
    `InventoryAccountId` int NULL,
    `ExpenseAccountId` int NULL,
    `VatAccountId` int NULL,
    `CashAccountId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_PurchaseInvoices` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_PurchaseInvoices_Suppliers_SupplierId` FOREIGN KEY (`SupplierId`) REFERENCES `Suppliers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `Addresses` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `CustomerId` int NOT NULL,
    `TitleAr` longtext CHARACTER SET utf8mb4 NOT NULL,
    `TitleEn` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Street` longtext CHARACTER SET utf8mb4 NOT NULL,
    `City` longtext CHARACTER SET utf8mb4 NOT NULL,
    `District` longtext CHARACTER SET utf8mb4 NULL,
    `BuildingNo` longtext CHARACTER SET utf8mb4 NULL,
    `Floor` longtext CHARACTER SET utf8mb4 NULL,
    `ApartmentNo` longtext CHARACTER SET utf8mb4 NULL,
    `AdditionalInfo` longtext CHARACTER SET utf8mb4 NULL,
    `Latitude` double NULL,
    `Longitude` double NULL,
    `IsDefault` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_Addresses` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Addresses_Customers_CustomerId` FOREIGN KEY (`CustomerId`) REFERENCES `Customers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `ReceiptVouchers` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `VoucherNumber` longtext CHARACTER SET utf8mb4 NOT NULL,
    `VoucherDate` datetime(6) NOT NULL,
    `Amount` decimal(18,2) NOT NULL,
    `CashAccountId` int NOT NULL,
    `FromAccountId` int NOT NULL,
    `CustomerId` int NULL,
    `PaymentMethod` int NOT NULL,
    `Reference` longtext CHARACTER SET utf8mb4 NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `JournalEntryId` int NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentUrl` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentPublicId` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_ReceiptVouchers` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ReceiptVouchers_Accounts_CashAccountId` FOREIGN KEY (`CashAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ReceiptVouchers_Accounts_FromAccountId` FOREIGN KEY (`FromAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ReceiptVouchers_Customers_CustomerId` FOREIGN KEY (`CustomerId`) REFERENCES `Customers` (`Id`),
    CONSTRAINT `FK_ReceiptVouchers_JournalEntries_JournalEntryId` FOREIGN KEY (`JournalEntryId`) REFERENCES `JournalEntries` (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `ProductImages` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ProductId` int NOT NULL,
    `ImageUrl` longtext CHARACTER SET utf8mb4 NOT NULL,
    `ImagePublicId` longtext CHARACTER SET utf8mb4 NULL,
    `IsMain` tinyint(1) NOT NULL,
    `ColorAr` longtext CHARACTER SET utf8mb4 NULL,
    `SortOrder` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_ProductImages` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ProductImages_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `ProductVariants` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ProductId` int NOT NULL,
    `Size` longtext CHARACTER SET utf8mb4 NULL,
    `Color` longtext CHARACTER SET utf8mb4 NULL,
    `ColorAr` longtext CHARACTER SET utf8mb4 NULL,
    `StockQuantity` int NOT NULL,
    `ReorderLevel` int NOT NULL,
    `PriceAdjustment` decimal(18,2) NULL,
    `ImageUrl` longtext CHARACTER SET utf8mb4 NULL,
    `ImagePublicId` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_ProductVariants` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ProductVariants_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `Reviews` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ProductId` int NOT NULL,
    `CustomerId` int NOT NULL,
    `Rating` int NOT NULL,
    `Comment` longtext CHARACTER SET utf8mb4 NULL,
    `IsApproved` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_Reviews` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Reviews_Customers_CustomerId` FOREIGN KEY (`CustomerId`) REFERENCES `Customers` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_Reviews_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `WishlistItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ProductId` int NOT NULL,
    `CustomerId` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_WishlistItems` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_WishlistItems_Customers_CustomerId` FOREIGN KEY (`CustomerId`) REFERENCES `Customers` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_WishlistItems_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `SupplierPayments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `PaymentNumber` longtext CHARACTER SET utf8mb4 NOT NULL,
    `SupplierId` int NOT NULL,
    `PurchaseInvoiceId` int NULL,
    `PaymentDate` datetime(6) NOT NULL,
    `Amount` decimal(18,2) NOT NULL,
    `PaymentMethod` int NOT NULL,
    `AccountName` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `ReferenceNumber` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentUrl` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentPublicId` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_SupplierPayments` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_SupplierPayments_PurchaseInvoices_PurchaseInvoiceId` FOREIGN KEY (`PurchaseInvoiceId`) REFERENCES `PurchaseInvoices` (`Id`),
    CONSTRAINT `FK_SupplierPayments_Suppliers_SupplierId` FOREIGN KEY (`SupplierId`) REFERENCES `Suppliers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `Orders` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `OrderNumber` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `CustomerId` int NOT NULL,
    `Status` int NOT NULL,
    `FulfillmentType` int NOT NULL,
    `PaymentMethod` int NOT NULL,
    `PaymentStatus` int NOT NULL,
    `Source` int NOT NULL,
    `DeliveryAddressId` int NULL,
    `DeliveryFee` decimal(18,2) NOT NULL,
    `EstimatedDeliveryDate` datetime(6) NULL,
    `ActualDeliveryDate` datetime(6) NULL,
    `DeliveryNotes` longtext CHARACTER SET utf8mb4 NULL,
    `PickupScheduledAt` datetime(6) NULL,
    `PickupConfirmedAt` datetime(6) NULL,
    `SubTotal` decimal(18,2) NOT NULL,
    `DiscountAmount` decimal(18,2) NOT NULL,
    `CouponCode` longtext CHARACTER SET utf8mb4 NULL,
    `TotalAmount` decimal(18,2) NOT NULL,
    `CustomerNotes` longtext CHARACTER SET utf8mb4 NULL,
    `AdminNotes` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentUrl` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentPublicId` longtext CHARACTER SET utf8mb4 NULL,
    `SalesPersonId` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_Orders` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Orders_Addresses_DeliveryAddressId` FOREIGN KEY (`DeliveryAddressId`) REFERENCES `Addresses` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_Orders_Customers_CustomerId` FOREIGN KEY (`CustomerId`) REFERENCES `Customers` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE TABLE `CartItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `CustomerId` int NOT NULL,
    `ProductId` int NOT NULL,
    `ProductVariantId` int NULL,
    `Quantity` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_CartItems` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_CartItems_Customers_CustomerId` FOREIGN KEY (`CustomerId`) REFERENCES `Customers` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_CartItems_ProductVariants_ProductVariantId` FOREIGN KEY (`ProductVariantId`) REFERENCES `ProductVariants` (`Id`),
    CONSTRAINT `FK_CartItems_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `PurchaseInvoiceItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `PurchaseInvoiceId` int NOT NULL,
    `ProductId` int NULL,
    `ProductVariantId` int NULL,
    `Description` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Unit` longtext CHARACTER SET utf8mb4 NULL,
    `Quantity` int NOT NULL,
    `UnitCost` decimal(18,2) NOT NULL,
    `TotalCost` decimal(18,2) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_PurchaseInvoiceItems` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_PurchaseInvoiceItems_ProductVariants_ProductVariantId` FOREIGN KEY (`ProductVariantId`) REFERENCES `ProductVariants` (`Id`),
    CONSTRAINT `FK_PurchaseInvoiceItems_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`),
    CONSTRAINT `FK_PurchaseInvoiceItems_PurchaseInvoices_PurchaseInvoiceId` FOREIGN KEY (`PurchaseInvoiceId`) REFERENCES `PurchaseInvoices` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `OrderItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `OrderId` int NOT NULL,
    `ProductId` int NOT NULL,
    `ProductVariantId` int NULL,
    `ProductNameAr` longtext CHARACTER SET utf8mb4 NOT NULL,
    `ProductNameEn` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Size` longtext CHARACTER SET utf8mb4 NULL,
    `Color` longtext CHARACTER SET utf8mb4 NULL,
    `Quantity` int NOT NULL,
    `UnitPrice` decimal(18,2) NOT NULL,
    `TotalPrice` decimal(18,2) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_OrderItems` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_OrderItems_Orders_OrderId` FOREIGN KEY (`OrderId`) REFERENCES `Orders` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_OrderItems_ProductVariants_ProductVariantId` FOREIGN KEY (`ProductVariantId`) REFERENCES `ProductVariants` (`Id`),
    CONSTRAINT `FK_OrderItems_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE TABLE `OrderStatusHistories` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `OrderId` int NOT NULL,
    `Status` int NOT NULL,
    `Note` longtext CHARACTER SET utf8mb4 NULL,
    `ChangedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_OrderStatusHistories` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_OrderStatusHistories_Orders_OrderId` FOREIGN KEY (`OrderId`) REFERENCES `Orders` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

INSERT INTO `Categories` (`Id`, `CreatedAt`, `DescriptionAr`, `DescriptionEn`, `ImageUrl`, `IsActive`, `IsDeleted`, `NameAr`, `NameEn`, `ParentId`, `Type`, `UpdatedAt`)
VALUES (1, TIMESTAMP '2024-01-01 00:00:00', NULL, NULL, NULL, TRUE, FALSE, 'رجالي', 'Men', NULL, 1, NULL),
(2, TIMESTAMP '2024-01-01 00:00:00', NULL, NULL, NULL, TRUE, FALSE, 'حريمي', 'Women', NULL, 2, NULL),
(3, TIMESTAMP '2024-01-01 00:00:00', NULL, NULL, NULL, TRUE, FALSE, 'أطفال', 'Kids', NULL, 3, NULL),
(4, TIMESTAMP '2024-01-01 00:00:00', NULL, NULL, NULL, TRUE, FALSE, 'أدوات رياضية', 'Sports Equipment', NULL, 4, NULL);

INSERT INTO `StoreSettings` (`StoreConfigId`, `FacebookPage`, `FixedDeliveryFee`, `FreeDeliveryAt`, `InMaintenance`, `InstagramPage`, `LastUpdateDate`, `StoreBrandName`, `StoreEmailAddr`, `StorePhoneNo`, `StorePhysicalAddr`, `StoreSlogan`, `StoreWhatsAppNo`, `TikTokPage`, `VatRatePercent`)
VALUES (1, '', 50.0, 2000.0, FALSE, '', TIMESTAMP '2024-01-01 00:00:00', 'Sportive', '', '', '', 'Your Ultimate Sports Destination', '', '', 14.0);

CREATE UNIQUE INDEX `IX_Accounts_Code` ON `Accounts` (`Code`);

CREATE INDEX `IX_Accounts_ParentId` ON `Accounts` (`ParentId`);

CREATE INDEX `IX_AccountSystemMappings_AccountId` ON `AccountSystemMappings` (`AccountId`);

CREATE UNIQUE INDEX `IX_AccountSystemMappings_Key` ON `AccountSystemMappings` (`Key`);

CREATE INDEX `IX_Addresses_CustomerId` ON `Addresses` (`CustomerId`);

CREATE INDEX `IX_AspNetRoleClaims_RoleId` ON `AspNetRoleClaims` (`RoleId`);

CREATE UNIQUE INDEX `RoleNameIndex` ON `AspNetRoles` (`NormalizedName`);

CREATE INDEX `IX_AspNetUserClaims_UserId` ON `AspNetUserClaims` (`UserId`);

CREATE INDEX `IX_AspNetUserLogins_UserId` ON `AspNetUserLogins` (`UserId`);

CREATE INDEX `IX_AspNetUserRoles_RoleId` ON `AspNetUserRoles` (`RoleId`);

CREATE INDEX `EmailIndex` ON `AspNetUsers` (`NormalizedEmail`);

CREATE INDEX `IX_AspNetUsers_PhoneNumber` ON `AspNetUsers` (`PhoneNumber`);

CREATE UNIQUE INDEX `UserNameIndex` ON `AspNetUsers` (`NormalizedUserName`);

CREATE INDEX `IX_CartItems_CustomerId` ON `CartItems` (`CustomerId`);

CREATE INDEX `IX_CartItems_ProductId` ON `CartItems` (`ProductId`);

CREATE INDEX `IX_CartItems_ProductVariantId` ON `CartItems` (`ProductVariantId`);

CREATE INDEX `IX_Categories_ParentId` ON `Categories` (`ParentId`);

CREATE UNIQUE INDEX `IX_Coupons_Code` ON `Coupons` (`Code`);

CREATE INDEX `IX_Customers_AppUserId` ON `Customers` (`AppUserId`);

CREATE UNIQUE INDEX `IX_Customers_Email` ON `Customers` (`Email`);

CREATE INDEX `IX_Customers_Phone` ON `Customers` (`Phone`);

CREATE INDEX `IX_JournalEntries_ReversalOfId` ON `JournalEntries` (`ReversalOfId`);

CREATE INDEX `IX_JournalLines_AccountId` ON `JournalLines` (`AccountId`);

CREATE INDEX `IX_JournalLines_JournalEntryId` ON `JournalLines` (`JournalEntryId`);

CREATE INDEX `IX_Notifications_UserId` ON `Notifications` (`UserId`);

CREATE INDEX `IX_Notifications_UserId_IsRead` ON `Notifications` (`UserId`, `IsRead`);

CREATE INDEX `IX_OrderItems_OrderId` ON `OrderItems` (`OrderId`);

CREATE INDEX `IX_OrderItems_ProductId` ON `OrderItems` (`ProductId`);

CREATE INDEX `IX_OrderItems_ProductVariantId` ON `OrderItems` (`ProductVariantId`);

CREATE INDEX `IX_Orders_CustomerId` ON `Orders` (`CustomerId`);

CREATE INDEX `IX_Orders_DeliveryAddressId` ON `Orders` (`DeliveryAddressId`);

CREATE UNIQUE INDEX `IX_Orders_OrderNumber` ON `Orders` (`OrderNumber`);

CREATE INDEX `IX_OrderStatusHistories_OrderId` ON `OrderStatusHistories` (`OrderId`);

CREATE INDEX `IX_PaymentVouchers_CashAccountId` ON `PaymentVouchers` (`CashAccountId`);

CREATE INDEX `IX_PaymentVouchers_JournalEntryId` ON `PaymentVouchers` (`JournalEntryId`);

CREATE INDEX `IX_PaymentVouchers_SupplierId` ON `PaymentVouchers` (`SupplierId`);

CREATE INDEX `IX_PaymentVouchers_ToAccountId` ON `PaymentVouchers` (`ToAccountId`);

CREATE INDEX `IX_ProductImages_ProductId` ON `ProductImages` (`ProductId`);

CREATE INDEX `IX_Products_CategoryId` ON `Products` (`CategoryId`);

CREATE UNIQUE INDEX `IX_Products_SKU` ON `Products` (`SKU`);

CREATE INDEX `IX_ProductVariants_ProductId` ON `ProductVariants` (`ProductId`);

CREATE INDEX `IX_PurchaseInvoiceItems_ProductId` ON `PurchaseInvoiceItems` (`ProductId`);

CREATE INDEX `IX_PurchaseInvoiceItems_ProductVariantId` ON `PurchaseInvoiceItems` (`ProductVariantId`);

CREATE INDEX `IX_PurchaseInvoiceItems_PurchaseInvoiceId` ON `PurchaseInvoiceItems` (`PurchaseInvoiceId`);

CREATE INDEX `IX_PurchaseInvoices_SupplierId` ON `PurchaseInvoices` (`SupplierId`);

CREATE INDEX `IX_ReceiptVouchers_CashAccountId` ON `ReceiptVouchers` (`CashAccountId`);

CREATE INDEX `IX_ReceiptVouchers_CustomerId` ON `ReceiptVouchers` (`CustomerId`);

CREATE INDEX `IX_ReceiptVouchers_FromAccountId` ON `ReceiptVouchers` (`FromAccountId`);

CREATE INDEX `IX_ReceiptVouchers_JournalEntryId` ON `ReceiptVouchers` (`JournalEntryId`);

CREATE INDEX `IX_Reviews_CustomerId` ON `Reviews` (`CustomerId`);

CREATE INDEX `IX_Reviews_ProductId` ON `Reviews` (`ProductId`);

CREATE INDEX `IX_SupplierPayments_PurchaseInvoiceId` ON `SupplierPayments` (`PurchaseInvoiceId`);

CREATE INDEX `IX_SupplierPayments_SupplierId` ON `SupplierPayments` (`SupplierId`);

CREATE INDEX `IX_Suppliers_MainAccountId` ON `Suppliers` (`MainAccountId`);

CREATE INDEX `IX_UserModulePermissions_UserAccountID` ON `UserModulePermissions` (`UserAccountID`);

CREATE UNIQUE INDEX `IX_WishlistItems_CustomerId_ProductId` ON `WishlistItems` (`CustomerId`, `ProductId`);

CREATE INDEX `IX_WishlistItems_ProductId` ON `WishlistItems` (`ProductId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260330012212_InitialCreate', '9.0.0');

ALTER TABLE `Categories` MODIFY COLUMN `NameEn` varchar(150) CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `Categories` MODIFY COLUMN `NameAr` varchar(150) CHARACTER SET utf8mb4 NOT NULL;

CREATE TABLE `Brands` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `NameAr` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `NameEn` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `DescriptionAr` longtext CHARACTER SET utf8mb4 NULL,
    `DescriptionEn` longtext CHARACTER SET utf8mb4 NULL,
    `ImageUrl` longtext CHARACTER SET utf8mb4 NULL,
    `IsActive` tinyint(1) NOT NULL,
    `ParentId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_Brands` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Brands_Brands_ParentId` FOREIGN KEY (`ParentId`) REFERENCES `Brands` (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `InventoryAudits` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Title` longtext CHARACTER SET utf8mb4 NOT NULL,
    `AuditDate` datetime(6) NOT NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `Status` int NOT NULL,
    `TotalExpectedValue` decimal(65,30) NOT NULL,
    `TotalActualValue` decimal(65,30) NOT NULL,
    `JournalEntryId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_InventoryAudits` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_InventoryAudits_JournalEntries_JournalEntryId` FOREIGN KEY (`JournalEntryId`) REFERENCES `JournalEntries` (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `InventoryAuditItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `InventoryAuditId` int NOT NULL,
    `ProductId` int NULL,
    `ProductVariantId` int NULL,
    `ExpectedQuantity` int NOT NULL,
    `ActualQuantity` int NOT NULL,
    `UnitCost` decimal(65,30) NOT NULL,
    `Note` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_InventoryAuditItems` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_InventoryAuditItems_InventoryAudits_InventoryAuditId` FOREIGN KEY (`InventoryAuditId`) REFERENCES `InventoryAudits` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_InventoryAuditItems_ProductVariants_ProductVariantId` FOREIGN KEY (`ProductVariantId`) REFERENCES `ProductVariants` (`Id`),
    CONSTRAINT `FK_InventoryAuditItems_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`)
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_Products_BrandId` ON `Products` (`BrandId`);

CREATE INDEX `IX_Brands_ParentId` ON `Brands` (`ParentId`);

CREATE INDEX `IX_InventoryAuditItems_InventoryAuditId` ON `InventoryAuditItems` (`InventoryAuditId`);

CREATE INDEX `IX_InventoryAuditItems_ProductId` ON `InventoryAuditItems` (`ProductId`);

CREATE INDEX `IX_InventoryAuditItems_ProductVariantId` ON `InventoryAuditItems` (`ProductVariantId`);

CREATE INDEX `IX_InventoryAudits_JournalEntryId` ON `InventoryAudits` (`JournalEntryId`);

ALTER TABLE `Products` ADD CONSTRAINT `FK_Products_Brands_BrandId` FOREIGN KEY (`BrandId`) REFERENCES `Brands` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260331014903_AddParentToBrandAndRefactorProductBrand', '9.0.0');

ALTER TABLE `StoreSettings` ADD `DeliveryRevenueAccountId` longtext CHARACTER SET utf8mb4 NULL;

CREATE TABLE `InventoryMovements` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ProductId` int NULL,
    `ProductVariantId` int NULL,
    `Type` int NOT NULL,
    `Quantity` int NOT NULL,
    `RemainingStock` int NOT NULL,
    `Reference` longtext CHARACTER SET utf8mb4 NULL,
    `Note` longtext CHARACTER SET utf8mb4 NULL,
    `UnitCost` decimal(65,30) NOT NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    CONSTRAINT `PK_InventoryMovements` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_InventoryMovements_ProductVariants_ProductVariantId` FOREIGN KEY (`ProductVariantId`) REFERENCES `ProductVariants` (`Id`),
    CONSTRAINT `FK_InventoryMovements_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`)
) CHARACTER SET=utf8mb4;

UPDATE `StoreSettings` SET `DeliveryRevenueAccountId` = NULL
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


CREATE INDEX `IX_InventoryMovements_ProductId` ON `InventoryMovements` (`ProductId`);

CREATE INDEX `IX_InventoryMovements_ProductVariantId` ON `InventoryMovements` (`ProductVariantId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260331032120_AddDeliveryRevenueAccount', '9.0.0');

ALTER TABLE `Products` MODIFY COLUMN `CostPrice` decimal(18,2) NULL;

ALTER TABLE `Products` ADD `VatRate` decimal(18,2) NULL;

ALTER TABLE `OrderStatusHistories` MODIFY COLUMN `Status` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `Orders` MODIFY COLUMN `Status` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `Orders` MODIFY COLUMN `Source` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `Orders` MODIFY COLUMN `PaymentStatus` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `Orders` MODIFY COLUMN `PaymentMethod` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `Orders` MODIFY COLUMN `FulfillmentType` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `Orders` ADD `TotalVatAmount` decimal(18,2) NOT NULL DEFAULT 0.0;

ALTER TABLE `OrderItems` ADD `HasTax` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `OrderItems` ADD `ItemVatAmount` decimal(18,2) NOT NULL DEFAULT 0.0;

ALTER TABLE `OrderItems` ADD `VatRateApplied` decimal(18,2) NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260401051342_AddTaxGranularityFields', '9.0.0');

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260401150256_AddUniqueConstraints', '9.0.0');

ALTER TABLE `Customers` ADD `MainAccountId` int NULL;

CREATE INDEX `IX_Customers_MainAccountId` ON `Customers` (`MainAccountId`);

ALTER TABLE `Customers` ADD CONSTRAINT `FK_Customers_Accounts_MainAccountId` FOREIGN KEY (`MainAccountId`) REFERENCES `Accounts` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260402025807_AddCustomerMainAccount', '9.0.0');

ALTER TABLE `SupplierPayments` MODIFY COLUMN `PaymentMethod` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `ReceiptVouchers` MODIFY COLUMN `PaymentMethod` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `PurchaseInvoices` MODIFY COLUMN `Status` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `PurchaseInvoices` MODIFY COLUMN `PaymentTerms` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `Products` MODIFY COLUMN `Status` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `PaymentVouchers` MODIFY COLUMN `PaymentMethod` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `JournalEntries` MODIFY COLUMN `Type` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `JournalEntries` MODIFY COLUMN `Status` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `Accounts` MODIFY COLUMN `Type` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `Accounts` MODIFY COLUMN `Nature` longtext CHARACTER SET utf8mb4 NOT NULL;

CREATE TABLE `AuditLogs` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `UserId` varchar(450) CHARACTER SET utf8mb4 NULL,
    `UserName` varchar(200) CHARACTER SET utf8mb4 NULL,
    `Action` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
    `EntityType` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `EntityId` varchar(100) CHARACTER SET utf8mb4 NULL,
    `OldValues` longtext CHARACTER SET utf8mb4 NULL,
    `NewValues` longtext CHARACTER SET utf8mb4 NULL,
    `Notes` varchar(500) CHARACTER SET utf8mb4 NULL,
    `IpAddress` varchar(50) CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_AuditLogs` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_AuditLogs_CreatedAt` ON `AuditLogs` (`CreatedAt`);

CREATE INDEX `IX_AuditLogs_EntityType` ON `AuditLogs` (`EntityType`);

CREATE INDEX `IX_AuditLogs_EntityType_EntityId` ON `AuditLogs` (`EntityType`, `EntityId`);

CREATE INDEX `IX_AuditLogs_UserId` ON `AuditLogs` (`UserId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260403220213_SyncEnumsToStrings', '9.0.0');

CREATE INDEX `IX_JournalLines_CustomerId` ON `JournalLines` (`CustomerId`);

CREATE INDEX `IX_JournalLines_SupplierId` ON `JournalLines` (`SupplierId`);

ALTER TABLE `JournalLines` ADD CONSTRAINT `FK_JournalLines_Customers_CustomerId` FOREIGN KEY (`CustomerId`) REFERENCES `Customers` (`Id`);

ALTER TABLE `JournalLines` ADD CONSTRAINT `FK_JournalLines_Suppliers_SupplierId` FOREIGN KEY (`SupplierId`) REFERENCES `Suppliers` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260404105511_FixModelDiscrepancy', '9.0.0');

ALTER TABLE `Customers` ADD `FixedDiscount` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `AspNetUsers` ADD `FixedDiscount` decimal(65,30) NOT NULL DEFAULT 0.0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260405002326_AddFixedDiscountToCustomer', '9.0.0');

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260405072111_AddFixedDiscountAndCostPrice', '9.0.0');

ALTER TABLE `OrderItems` ADD `ReturnedQuantity` int NOT NULL DEFAULT 0;

ALTER TABLE `JournalEntries` ADD `OrderId` int NULL;

CREATE INDEX `IX_JournalEntries_OrderId` ON `JournalEntries` (`OrderId`);

ALTER TABLE `JournalEntries` ADD CONSTRAINT `FK_JournalEntries_Orders_OrderId` FOREIGN KEY (`OrderId`) REFERENCES `Orders` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260406212629_AddReturnedQuantityToOrderItem', '9.0.0');

ALTER TABLE `Customers` DROP COLUMN `FirstName`;

ALTER TABLE `AspNetUsers` DROP COLUMN `FirstName`;

ALTER TABLE `Customers` RENAME COLUMN `LastName` TO `FullName`;

ALTER TABLE `AspNetUsers` RENAME COLUMN `LastName` TO `FullName`;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260407003245_ConsolidateNameFields', '9.0.0');


                    SET @col_exists_WishlistItems = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'WishlistItems'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_WishlistItems = IF(
                        @col_exists_WishlistItems > 0,
                        'ALTER TABLE `WishlistItems` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_WishlistItems FROM @sql_WishlistItems;
                    EXECUTE safe_stmt_WishlistItems;
                    DEALLOCATE PREPARE safe_stmt_WishlistItems;
                


                    SET @col_exists_UserModulePermissions = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'UserModulePermissions'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_UserModulePermissions = IF(
                        @col_exists_UserModulePermissions > 0,
                        'ALTER TABLE `UserModulePermissions` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_UserModulePermissions FROM @sql_UserModulePermissions;
                    EXECUTE safe_stmt_UserModulePermissions;
                    DEALLOCATE PREPARE safe_stmt_UserModulePermissions;
                


                    SET @col_exists_Suppliers = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'Suppliers'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_Suppliers = IF(
                        @col_exists_Suppliers > 0,
                        'ALTER TABLE `Suppliers` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_Suppliers FROM @sql_Suppliers;
                    EXECUTE safe_stmt_Suppliers;
                    DEALLOCATE PREPARE safe_stmt_Suppliers;
                


                    SET @col_exists_SupplierPayments = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'SupplierPayments'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_SupplierPayments = IF(
                        @col_exists_SupplierPayments > 0,
                        'ALTER TABLE `SupplierPayments` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_SupplierPayments FROM @sql_SupplierPayments;
                    EXECUTE safe_stmt_SupplierPayments;
                    DEALLOCATE PREPARE safe_stmt_SupplierPayments;
                


                    SET @col_exists_Reviews = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'Reviews'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_Reviews = IF(
                        @col_exists_Reviews > 0,
                        'ALTER TABLE `Reviews` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_Reviews FROM @sql_Reviews;
                    EXECUTE safe_stmt_Reviews;
                    DEALLOCATE PREPARE safe_stmt_Reviews;
                


                    SET @col_exists_ReceiptVouchers = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'ReceiptVouchers'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_ReceiptVouchers = IF(
                        @col_exists_ReceiptVouchers > 0,
                        'ALTER TABLE `ReceiptVouchers` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_ReceiptVouchers FROM @sql_ReceiptVouchers;
                    EXECUTE safe_stmt_ReceiptVouchers;
                    DEALLOCATE PREPARE safe_stmt_ReceiptVouchers;
                


                    SET @col_exists_PurchaseInvoices = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'PurchaseInvoices'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_PurchaseInvoices = IF(
                        @col_exists_PurchaseInvoices > 0,
                        'ALTER TABLE `PurchaseInvoices` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_PurchaseInvoices FROM @sql_PurchaseInvoices;
                    EXECUTE safe_stmt_PurchaseInvoices;
                    DEALLOCATE PREPARE safe_stmt_PurchaseInvoices;
                


                    SET @col_exists_PurchaseInvoiceItems = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'PurchaseInvoiceItems'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_PurchaseInvoiceItems = IF(
                        @col_exists_PurchaseInvoiceItems > 0,
                        'ALTER TABLE `PurchaseInvoiceItems` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_PurchaseInvoiceItems FROM @sql_PurchaseInvoiceItems;
                    EXECUTE safe_stmt_PurchaseInvoiceItems;
                    DEALLOCATE PREPARE safe_stmt_PurchaseInvoiceItems;
                


                    SET @col_exists_ProductVariants = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'ProductVariants'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_ProductVariants = IF(
                        @col_exists_ProductVariants > 0,
                        'ALTER TABLE `ProductVariants` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_ProductVariants FROM @sql_ProductVariants;
                    EXECUTE safe_stmt_ProductVariants;
                    DEALLOCATE PREPARE safe_stmt_ProductVariants;
                


                    SET @col_exists_Products = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'Products'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_Products = IF(
                        @col_exists_Products > 0,
                        'ALTER TABLE `Products` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_Products FROM @sql_Products;
                    EXECUTE safe_stmt_Products;
                    DEALLOCATE PREPARE safe_stmt_Products;
                


                    SET @col_exists_ProductImages = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'ProductImages'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_ProductImages = IF(
                        @col_exists_ProductImages > 0,
                        'ALTER TABLE `ProductImages` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_ProductImages FROM @sql_ProductImages;
                    EXECUTE safe_stmt_ProductImages;
                    DEALLOCATE PREPARE safe_stmt_ProductImages;
                


                    SET @col_exists_PaymentVouchers = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'PaymentVouchers'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_PaymentVouchers = IF(
                        @col_exists_PaymentVouchers > 0,
                        'ALTER TABLE `PaymentVouchers` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_PaymentVouchers FROM @sql_PaymentVouchers;
                    EXECUTE safe_stmt_PaymentVouchers;
                    DEALLOCATE PREPARE safe_stmt_PaymentVouchers;
                


                    SET @col_exists_OrderStatusHistories = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'OrderStatusHistories'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_OrderStatusHistories = IF(
                        @col_exists_OrderStatusHistories > 0,
                        'ALTER TABLE `OrderStatusHistories` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_OrderStatusHistories FROM @sql_OrderStatusHistories;
                    EXECUTE safe_stmt_OrderStatusHistories;
                    DEALLOCATE PREPARE safe_stmt_OrderStatusHistories;
                


                    SET @col_exists_Orders = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'Orders'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_Orders = IF(
                        @col_exists_Orders > 0,
                        'ALTER TABLE `Orders` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_Orders FROM @sql_Orders;
                    EXECUTE safe_stmt_Orders;
                    DEALLOCATE PREPARE safe_stmt_Orders;
                


                    SET @col_exists_OrderItems = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'OrderItems'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_OrderItems = IF(
                        @col_exists_OrderItems > 0,
                        'ALTER TABLE `OrderItems` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_OrderItems FROM @sql_OrderItems;
                    EXECUTE safe_stmt_OrderItems;
                    DEALLOCATE PREPARE safe_stmt_OrderItems;
                


                    SET @col_exists_Notifications = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'Notifications'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_Notifications = IF(
                        @col_exists_Notifications > 0,
                        'ALTER TABLE `Notifications` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_Notifications FROM @sql_Notifications;
                    EXECUTE safe_stmt_Notifications;
                    DEALLOCATE PREPARE safe_stmt_Notifications;
                


                    SET @col_exists_JournalLines = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'JournalLines'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_JournalLines = IF(
                        @col_exists_JournalLines > 0,
                        'ALTER TABLE `JournalLines` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_JournalLines FROM @sql_JournalLines;
                    EXECUTE safe_stmt_JournalLines;
                    DEALLOCATE PREPARE safe_stmt_JournalLines;
                


                    SET @col_exists_JournalEntries = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'JournalEntries'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_JournalEntries = IF(
                        @col_exists_JournalEntries > 0,
                        'ALTER TABLE `JournalEntries` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_JournalEntries FROM @sql_JournalEntries;
                    EXECUTE safe_stmt_JournalEntries;
                    DEALLOCATE PREPARE safe_stmt_JournalEntries;
                


                    SET @col_exists_InventoryMovements = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'InventoryMovements'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_InventoryMovements = IF(
                        @col_exists_InventoryMovements > 0,
                        'ALTER TABLE `InventoryMovements` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_InventoryMovements FROM @sql_InventoryMovements;
                    EXECUTE safe_stmt_InventoryMovements;
                    DEALLOCATE PREPARE safe_stmt_InventoryMovements;
                


                    SET @col_exists_InventoryAudits = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'InventoryAudits'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_InventoryAudits = IF(
                        @col_exists_InventoryAudits > 0,
                        'ALTER TABLE `InventoryAudits` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_InventoryAudits FROM @sql_InventoryAudits;
                    EXECUTE safe_stmt_InventoryAudits;
                    DEALLOCATE PREPARE safe_stmt_InventoryAudits;
                


                    SET @col_exists_InventoryAuditItems = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'InventoryAuditItems'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_InventoryAuditItems = IF(
                        @col_exists_InventoryAuditItems > 0,
                        'ALTER TABLE `InventoryAuditItems` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_InventoryAuditItems FROM @sql_InventoryAuditItems;
                    EXECUTE safe_stmt_InventoryAuditItems;
                    DEALLOCATE PREPARE safe_stmt_InventoryAuditItems;
                


                    SET @col_exists_Customers = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'Customers'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_Customers = IF(
                        @col_exists_Customers > 0,
                        'ALTER TABLE `Customers` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_Customers FROM @sql_Customers;
                    EXECUTE safe_stmt_Customers;
                    DEALLOCATE PREPARE safe_stmt_Customers;
                


                    SET @col_exists_Coupons = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'Coupons'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_Coupons = IF(
                        @col_exists_Coupons > 0,
                        'ALTER TABLE `Coupons` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_Coupons FROM @sql_Coupons;
                    EXECUTE safe_stmt_Coupons;
                    DEALLOCATE PREPARE safe_stmt_Coupons;
                


                    SET @col_exists_Categories = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'Categories'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_Categories = IF(
                        @col_exists_Categories > 0,
                        'ALTER TABLE `Categories` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_Categories FROM @sql_Categories;
                    EXECUTE safe_stmt_Categories;
                    DEALLOCATE PREPARE safe_stmt_Categories;
                


                    SET @col_exists_CartItems = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'CartItems'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_CartItems = IF(
                        @col_exists_CartItems > 0,
                        'ALTER TABLE `CartItems` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_CartItems FROM @sql_CartItems;
                    EXECUTE safe_stmt_CartItems;
                    DEALLOCATE PREPARE safe_stmt_CartItems;
                


                    SET @col_exists_Brands = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'Brands'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_Brands = IF(
                        @col_exists_Brands > 0,
                        'ALTER TABLE `Brands` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_Brands FROM @sql_Brands;
                    EXECUTE safe_stmt_Brands;
                    DEALLOCATE PREPARE safe_stmt_Brands;
                


                    SET @col_exists_BackupRecords = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'BackupRecords'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_BackupRecords = IF(
                        @col_exists_BackupRecords > 0,
                        'ALTER TABLE `BackupRecords` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_BackupRecords FROM @sql_BackupRecords;
                    EXECUTE safe_stmt_BackupRecords;
                    DEALLOCATE PREPARE safe_stmt_BackupRecords;
                


                    SET @col_exists_Addresses = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'Addresses'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_Addresses = IF(
                        @col_exists_Addresses > 0,
                        'ALTER TABLE `Addresses` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_Addresses FROM @sql_Addresses;
                    EXECUTE safe_stmt_Addresses;
                    DEALLOCATE PREPARE safe_stmt_Addresses;
                


                    SET @col_exists_AccountSystemMappings = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'AccountSystemMappings'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_AccountSystemMappings = IF(
                        @col_exists_AccountSystemMappings > 0,
                        'ALTER TABLE `AccountSystemMappings` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_AccountSystemMappings FROM @sql_AccountSystemMappings;
                    EXECUTE safe_stmt_AccountSystemMappings;
                    DEALLOCATE PREPARE safe_stmt_AccountSystemMappings;
                


                    SET @col_exists_Accounts = (
                        SELECT COUNT(*)
                        FROM information_schema.COLUMNS
                        WHERE TABLE_SCHEMA = DATABASE()
                          AND TABLE_NAME   = 'Accounts'
                          AND COLUMN_NAME  = 'IsDeleted'
                    );
                    SET @sql_Accounts = IF(
                        @col_exists_Accounts > 0,
                        'ALTER TABLE `Accounts` DROP COLUMN `IsDeleted`',
                        'SELECT 1'
                    );
                    PREPARE safe_stmt_Accounts FROM @sql_Accounts;
                    EXECUTE safe_stmt_Accounts;
                    DEALLOCATE PREPARE safe_stmt_Accounts;
                

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260407232856_RemoveIsDeletedColumn', '9.0.0');

ALTER TABLE `Categories` DROP FOREIGN KEY `FK_Categories_Categories_ParentId`;

ALTER TABLE `Categories` ADD CONSTRAINT `FK_Categories_Categories_ParentId` FOREIGN KEY (`ParentId`) REFERENCES `Categories` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260408003517_UpdateCategoryHierarchy', '9.0.0');

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260408003637_UpdateCategoryHierarchySelfReferencing', '9.0.0');

ALTER TABLE `Brands` DROP FOREIGN KEY `FK_Brands_Brands_ParentId`;

ALTER TABLE `Products` DROP FOREIGN KEY `FK_Products_Categories_CategoryId`;

ALTER TABLE `Products` MODIFY COLUMN `CategoryId` int NULL;

ALTER TABLE `Brands` ADD CONSTRAINT `FK_Brands_Brands_ParentId` FOREIGN KEY (`ParentId`) REFERENCES `Brands` (`Id`) ON DELETE RESTRICT;

ALTER TABLE `Products` ADD CONSTRAINT `FK_Products_Categories_CategoryId` FOREIGN KEY (`CategoryId`) REFERENCES `Categories` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260408010109_MakeCategoryNullableAndSetNullOnDelete', '9.0.0');

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260408010357_UpdateCategoryBrandDeleteBehavior', '9.0.0');

ALTER TABLE `InventoryMovements` DROP FOREIGN KEY `FK_InventoryMovements_ProductVariants_ProductVariantId`;

ALTER TABLE `InventoryMovements` DROP FOREIGN KEY `FK_InventoryMovements_Products_ProductId`;

ALTER TABLE `OrderItems` DROP FOREIGN KEY `FK_OrderItems_Products_ProductId`;

ALTER TABLE `Orders` ADD `PaidAmount` decimal(18,2) NOT NULL DEFAULT 0.0;

ALTER TABLE `OrderItems` MODIFY COLUMN `ProductId` int NULL;

ALTER TABLE `InventoryMovements` MODIFY COLUMN `UnitCost` decimal(18,2) NOT NULL;

ALTER TABLE `InventoryMovements` MODIFY COLUMN `Type` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `CartItems` MODIFY COLUMN `ProductId` int NULL;

ALTER TABLE `InventoryMovements` ADD CONSTRAINT `FK_InventoryMovements_ProductVariants_ProductVariantId` FOREIGN KEY (`ProductVariantId`) REFERENCES `ProductVariants` (`Id`) ON DELETE SET NULL;

ALTER TABLE `InventoryMovements` ADD CONSTRAINT `FK_InventoryMovements_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE SET NULL;

ALTER TABLE `OrderItems` ADD CONSTRAINT `FK_OrderItems_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260408110135_AddPaidAmountToOrder', '9.0.0');

ALTER TABLE `Categories` ADD `Type` int NOT NULL DEFAULT 0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260408132334_AddCategoryTypeAndStockUpdates', '9.0.0');

ALTER TABLE `StoreSettings` ADD `TimeZoneId` varchar(100) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';

UPDATE `StoreSettings` SET `TimeZoneId` = 'Egypt Standard Time'
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260409090937_AddTimeZoneIdToStoreSettings', '9.0.0');

ALTER TABLE `Orders` ADD `ArchivedAt` datetime(6) NULL;

ALTER TABLE `Orders` ADD `IsArchived` tinyint(1) NOT NULL DEFAULT FALSE;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260409094839_AddOrderArchiveFields', '9.0.0');

ALTER TABLE `ReceiptVouchers` ADD `OrderId` int NULL;

ALTER TABLE `PaymentVouchers` ADD `PurchaseInvoiceId` int NULL;

CREATE INDEX `IX_ReceiptVouchers_OrderId` ON `ReceiptVouchers` (`OrderId`);

CREATE INDEX `IX_PaymentVouchers_PurchaseInvoiceId` ON `PaymentVouchers` (`PurchaseInvoiceId`);

ALTER TABLE `PaymentVouchers` ADD CONSTRAINT `FK_PaymentVouchers_PurchaseInvoices_PurchaseInvoiceId` FOREIGN KEY (`PurchaseInvoiceId`) REFERENCES `PurchaseInvoices` (`Id`);

ALTER TABLE `ReceiptVouchers` ADD CONSTRAINT `FK_ReceiptVouchers_Orders_OrderId` FOREIGN KEY (`OrderId`) REFERENCES `Orders` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260409123954_AddOrderIdToReceiptVoucher', '9.0.0');

CREATE TABLE `CustomerInstallments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `CustomerId` int NOT NULL,
    `OrderId` int NULL,
    `TotalAmount` decimal(65,30) NOT NULL,
    `PaidAmount` decimal(65,30) NOT NULL,
    `DueDate` datetime(6) NOT NULL,
    `Note` longtext CHARACTER SET utf8mb4 NULL,
    `Status` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_CustomerInstallments` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_CustomerInstallments_Customers_CustomerId` FOREIGN KEY (`CustomerId`) REFERENCES `Customers` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_CustomerInstallments_Orders_OrderId` FOREIGN KEY (`OrderId`) REFERENCES `Orders` (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `InstallmentPayments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `CustomerInstallmentId` int NOT NULL,
    `Amount` decimal(65,30) NOT NULL,
    `PaymentDate` datetime(6) NOT NULL,
    `Note` longtext CHARACTER SET utf8mb4 NULL,
    `CollectedBy` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_InstallmentPayments` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_InstallmentPayments_CustomerInstallments_CustomerInstallment~` FOREIGN KEY (`CustomerInstallmentId`) REFERENCES `CustomerInstallments` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_CustomerInstallments_CustomerId` ON `CustomerInstallments` (`CustomerId`);

CREATE INDEX `IX_CustomerInstallments_OrderId` ON `CustomerInstallments` (`OrderId`);

CREATE INDEX `IX_InstallmentPayments_CustomerInstallmentId` ON `InstallmentPayments` (`CustomerInstallmentId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260409142242_AddInstallments', '9.0.0');

CREATE TABLE `ProductDiscounts` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ProductId` int NOT NULL,
    `DiscountType` int NOT NULL,
    `DiscountValue` decimal(65,30) NOT NULL,
    `MinQty` int NOT NULL,
    `ValidFrom` datetime(6) NOT NULL,
    `ValidTo` datetime(6) NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `Label` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_ProductDiscounts` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ProductDiscounts_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_ProductDiscounts_ProductId` ON `ProductDiscounts` (`ProductId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260409142952_AddProductDiscounts', '9.0.0');

ALTER TABLE `StoreSettings` MODIFY COLUMN `TikTokPage` varchar(500) CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `StoreSettings` MODIFY COLUMN `InstagramPage` varchar(500) CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `StoreSettings` MODIFY COLUMN `FacebookPage` varchar(500) CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `StoreSettings` ADD `AllowBackorders` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `AllowGuestCheckout` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `AllowedPaymentMethods` varchar(500) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';

ALTER TABLE `StoreSettings` ADD `AnnouncementEnabled` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `AnnouncementText` varchar(500) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `BrandColorH` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `BrandColorL` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `BrandColorS` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `CurrencyCode` varchar(10) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';

ALTER TABLE `StoreSettings` ADD `CurrencySymbol` varchar(10) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';

ALTER TABLE `StoreSettings` ADD `EnableCoupons` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `EnableReviews` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `FaviconUrl` varchar(500) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `HeroImageUrl` varchar(1000) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `HeroSubtitle` varchar(500) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `HeroTitle` varchar(200) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `HideOutOfStock` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `LogoUrl` varchar(500) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `LowStockThreshold` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `MinOrderAmount` decimal(18,2) NOT NULL DEFAULT 0.0;

ALTER TABLE `StoreSettings` ADD `OrderNumberPrefix` varchar(10) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';

ALTER TABLE `StoreSettings` ADD `ReceiptFooterText` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `ReceiptHeaderText` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `ReceiptShowBarcode` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `ReceiptShowLogo` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `ReviewsRequirePurchase` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `TwitterUrl` varchar(500) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `YoutubeUrl` varchar(500) CHARACTER SET utf8mb4 NULL;

CREATE TABLE `ShippingZones` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `NameAr` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `NameEn` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `Governorates` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Fee` decimal(18,2) NOT NULL,
    `FreeThreshold` decimal(18,2) NULL,
    `IsActive` tinyint(1) NOT NULL,
    `EstimatedDays` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_ShippingZones` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

UPDATE `StoreSettings` SET `AllowBackorders` = FALSE, `AllowGuestCheckout` = TRUE, `AllowedPaymentMethods` = 'Cash,Vodafone,InstaPay', `AnnouncementEnabled` = FALSE, `AnnouncementText` = NULL, `BrandColorH` = 221, `BrandColorL` = 53, `BrandColorS` = 83, `CurrencyCode` = 'EGP', `CurrencySymbol` = 'ج.م', `EnableCoupons` = TRUE, `EnableReviews` = TRUE, `FaviconUrl` = NULL, `HeroImageUrl` = NULL, `HeroSubtitle` = NULL, `HeroTitle` = NULL, `HideOutOfStock` = FALSE, `LogoUrl` = NULL, `LowStockThreshold` = 5, `MinOrderAmount` = 0.0, `OrderNumberPrefix` = 'SPT', `ReceiptFooterText` = NULL, `ReceiptHeaderText` = NULL, `ReceiptShowBarcode` = TRUE, `ReceiptShowLogo` = TRUE, `ReviewsRequirePurchase` = TRUE, `StoreSlogan` = 'Beyond Performance', `TwitterUrl` = NULL, `YoutubeUrl` = NULL
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260410052052_UpdateStoreSettingsV2', '9.0.0');

ALTER TABLE `Products` ADD `Slug` longtext CHARACTER SET utf8mb4 NOT NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260410214823_AddSlugToProduct', '9.0.0');

ALTER TABLE `Reviews` ADD `AdminReply` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `Reviews` ADD `RepliedAt` datetime(6) NULL;

ALTER TABLE `Reviews` ADD `RepliedBy` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `Products` ADD `AverageRating` double NOT NULL DEFAULT 0.0;

ALTER TABLE `Products` ADD `ReviewCount` int NOT NULL DEFAULT 0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260410223751_AddRatingCacheToProduct', '9.0.0');

CREATE TABLE `InventoryOpeningBalances` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Reference` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Date` datetime(6) NOT NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `TotalValue` decimal(18,2) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_InventoryOpeningBalances` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `InventoryOpeningBalanceItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `InventoryOpeningBalanceId` int NOT NULL,
    `ProductId` int NULL,
    `ProductVariantId` int NULL,
    `Quantity` int NOT NULL,
    `CostPrice` decimal(18,2) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_InventoryOpeningBalanceItems` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_InventoryOpeningBalanceItems_InventoryOpeningBalances_Invent~` FOREIGN KEY (`InventoryOpeningBalanceId`) REFERENCES `InventoryOpeningBalances` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_InventoryOpeningBalanceItems_ProductVariants_ProductVariantId` FOREIGN KEY (`ProductVariantId`) REFERENCES `ProductVariants` (`Id`),
    CONSTRAINT `FK_InventoryOpeningBalanceItems_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`)
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_InventoryOpeningBalanceItems_InventoryOpeningBalanceId` ON `InventoryOpeningBalanceItems` (`InventoryOpeningBalanceId`);

CREATE INDEX `IX_InventoryOpeningBalanceItems_ProductId` ON `InventoryOpeningBalanceItems` (`ProductId`);

CREATE INDEX `IX_InventoryOpeningBalanceItems_ProductVariantId` ON `InventoryOpeningBalanceItems` (`ProductVariantId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260413054028_AddInventoryOpeningBalances', '9.0.0');

CREATE TABLE `OrderPayments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `OrderId` int NOT NULL,
    `Method` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Amount` decimal(18,2) NOT NULL,
    `Reference` longtext CHARACTER SET utf8mb4 NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `IsPosted` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_OrderPayments` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_OrderPayments_Orders_OrderId` FOREIGN KEY (`OrderId`) REFERENCES `Orders` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_OrderPayments_OrderId` ON `OrderPayments` (`OrderId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260413111830_AddOrderPaymentsTable', '9.0.0');

ALTER TABLE `PurchaseInvoices` ADD `ReturnedAmount` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `PurchaseInvoiceItems` ADD `ReturnedQuantity` int NOT NULL DEFAULT 0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260414072609_AddPurchaseReturns', '9.0.0');

ALTER TABLE `Customers` ADD `Tags` longtext CHARACTER SET utf8mb4 NOT NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260414113121_AddCustomerTags', '9.0.0');

ALTER TABLE `Customers` ADD `Notes` longtext CHARACTER SET utf8mb4 NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260414120456_AddCustomerNotes', '9.0.0');

CREATE TABLE `PurchaseReturns` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ReturnNumber` longtext CHARACTER SET utf8mb4 NOT NULL,
    `PurchaseInvoiceId` int NOT NULL,
    `SupplierId` int NOT NULL,
    `ReturnDate` datetime(6) NOT NULL,
    `SubTotal` decimal(18,2) NOT NULL,
    `TaxAmount` decimal(18,2) NOT NULL,
    `DiscountAmount` decimal(18,2) NOT NULL,
    `TotalAmount` decimal(18,2) NOT NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `ReferenceNumber` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_PurchaseReturns` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_PurchaseReturns_PurchaseInvoices_PurchaseInvoiceId` FOREIGN KEY (`PurchaseInvoiceId`) REFERENCES `PurchaseInvoices` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_PurchaseReturns_Suppliers_SupplierId` FOREIGN KEY (`SupplierId`) REFERENCES `Suppliers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `PurchaseReturnItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `PurchaseReturnId` int NOT NULL,
    `PurchaseInvoiceItemId` int NOT NULL,
    `ProductId` int NULL,
    `ProductVariantId` int NULL,
    `Quantity` int NOT NULL,
    `UnitCost` decimal(18,2) NOT NULL,
    `TotalCost` decimal(18,2) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_PurchaseReturnItems` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_PurchaseReturnItems_ProductVariants_ProductVariantId` FOREIGN KEY (`ProductVariantId`) REFERENCES `ProductVariants` (`Id`),
    CONSTRAINT `FK_PurchaseReturnItems_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`),
    CONSTRAINT `FK_PurchaseReturnItems_PurchaseInvoiceItems_PurchaseInvoiceItem~` FOREIGN KEY (`PurchaseInvoiceItemId`) REFERENCES `PurchaseInvoiceItems` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_PurchaseReturnItems_PurchaseReturns_PurchaseReturnId` FOREIGN KEY (`PurchaseReturnId`) REFERENCES `PurchaseReturns` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_PurchaseReturnItems_ProductId` ON `PurchaseReturnItems` (`ProductId`);

CREATE INDEX `IX_PurchaseReturnItems_ProductVariantId` ON `PurchaseReturnItems` (`ProductVariantId`);

CREATE INDEX `IX_PurchaseReturnItems_PurchaseInvoiceItemId` ON `PurchaseReturnItems` (`PurchaseInvoiceItemId`);

CREATE INDEX `IX_PurchaseReturnItems_PurchaseReturnId` ON `PurchaseReturnItems` (`PurchaseReturnId`);

CREATE INDEX `IX_PurchaseReturns_PurchaseInvoiceId` ON `PurchaseReturns` (`PurchaseInvoiceId`);

CREATE INDEX `IX_PurchaseReturns_SupplierId` ON `PurchaseReturns` (`SupplierId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260414132415_AddPurchaseReturnsStandalone', '9.0.0');

CREATE TABLE `ProductUnits` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `NameAr` longtext CHARACTER SET utf8mb4 NOT NULL,
    `NameEn` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Symbol` longtext CHARACTER SET utf8mb4 NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_ProductUnits` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260414134000_AddProductUnits', '9.0.0');

ALTER TABLE `ProductUnits` ADD `Multiplier` decimal(65,30) NOT NULL DEFAULT 0.0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260414140859_AddUnitMultiplier', '9.0.0');

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260414161449_FixPurchaseReturnsTable', '9.0.0');

ALTER TABLE `PurchaseReturnItems` MODIFY COLUMN `Quantity` decimal(65,30) NOT NULL;

ALTER TABLE `PurchaseInvoiceItems` MODIFY COLUMN `ReturnedQuantity` decimal(65,30) NOT NULL;

ALTER TABLE `PurchaseInvoiceItems` MODIFY COLUMN `Quantity` decimal(65,30) NOT NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260414164531_UpdatePurchaseQuantityToDecimalV2', '9.0.0');

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260414164622_decimal_quantities', '9.0.0');

ALTER TABLE `Orders` ADD `ActualDeliveryCost` decimal(65,30) NOT NULL DEFAULT 0.0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260414233759_AddActualDeliveryCost', '9.0.0');

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260415073352_AddActualDeliveryCostToOrder', '9.0.0');

ALTER TABLE `StoreSettings` ADD `ReceiptComplaintsPhone` varchar(50) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `ReceiptShowAddress` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `ReceiptShowBalance` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `ReceiptShowCustomerDetails` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `ReceiptShowItemCount` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `ReceiptShowPhone` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `ReceiptShowTotalPieceCount` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `ReceiptSoftwareProvider` varchar(200) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `ReceiptTermsAndConditions` varchar(2000) CHARACTER SET utf8mb4 NULL;

UPDATE `StoreSettings` SET `ReceiptComplaintsPhone` = NULL, `ReceiptShowAddress` = TRUE, `ReceiptShowBalance` = TRUE, `ReceiptShowCustomerDetails` = TRUE, `ReceiptShowItemCount` = TRUE, `ReceiptShowPhone` = TRUE, `ReceiptShowTotalPieceCount` = TRUE, `ReceiptSoftwareProvider` = 'By Easy Store', `ReceiptTermsAndConditions` = NULL
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260415142933_AddReceiptCustomizationFields', '9.0.0');

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260415153618_AddUnitIdToProduct', '9.0.0');

ALTER TABLE `Customers` ADD `TotalPaid` decimal(18,2) NOT NULL DEFAULT 0.0;

ALTER TABLE `Customers` ADD `TotalSales` decimal(18,2) NOT NULL DEFAULT 0.0;

UPDATE `StoreSettings` SET `StoreEmailAddr` = 'contact@sportive-sportwear.com'
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260416081430_UpdateCustomerAndStoreSettings', '9.0.0');

ALTER TABLE `JournalLines` ADD `PurchaseInvoiceId` int NULL;

ALTER TABLE `JournalEntries` ADD `PurchaseInvoiceId` int NULL;

CREATE INDEX `IX_JournalLines_PurchaseInvoiceId` ON `JournalLines` (`PurchaseInvoiceId`);

CREATE INDEX `IX_JournalEntries_PurchaseInvoiceId` ON `JournalEntries` (`PurchaseInvoiceId`);

ALTER TABLE `JournalEntries` ADD CONSTRAINT `FK_JournalEntries_PurchaseInvoices_PurchaseInvoiceId` FOREIGN KEY (`PurchaseInvoiceId`) REFERENCES `PurchaseInvoices` (`Id`);

ALTER TABLE `JournalLines` ADD CONSTRAINT `FK_JournalLines_PurchaseInvoices_PurchaseInvoiceId` FOREIGN KEY (`PurchaseInvoiceId`) REFERENCES `PurchaseInvoices` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260416095922_LinkJournalEntryToPurchaseInvoice', '9.0.0');

ALTER TABLE `Products` ADD `UnitId` int NULL;

CREATE INDEX `IX_Products_UnitId` ON `Products` (`UnitId`);

ALTER TABLE `Products` ADD CONSTRAINT `FK_Products_ProductUnits_UnitId` FOREIGN KEY (`UnitId`) REFERENCES `ProductUnits` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260416142352_AddUnitIdToProductV2', '9.0.0');

ALTER TABLE `SupplierPayments` ADD `CashAccountId` int NULL;

CREATE INDEX `IX_SupplierPayments_CashAccountId` ON `SupplierPayments` (`CashAccountId`);

ALTER TABLE `SupplierPayments` ADD CONSTRAINT `FK_SupplierPayments_Accounts_CashAccountId` FOREIGN KEY (`CashAccountId`) REFERENCES `Accounts` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260417063453_AddCashAccountIdToSupplierPayment', '9.0.0');

CREATE TABLE `FixedAssetCategories` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `IsActive` tinyint(1) NOT NULL,
    `AssetAccountId` int NULL,
    `AccumDepreciationAccountId` int NULL,
    `DepreciationExpenseAccountId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_FixedAssetCategories` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_FixedAssetCategories_Accounts_AccumDepreciationAccountId` FOREIGN KEY (`AccumDepreciationAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_FixedAssetCategories_Accounts_AssetAccountId` FOREIGN KEY (`AssetAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_FixedAssetCategories_Accounts_DepreciationExpenseAccountId` FOREIGN KEY (`DepreciationExpenseAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4;

CREATE TABLE `FixedAssets` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `AssetNumber` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `Name` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `CategoryId` int NOT NULL,
    `PurchaseDate` datetime(6) NOT NULL,
    `PurchaseCost` decimal(18,2) NOT NULL,
    `Supplier` longtext CHARACTER SET utf8mb4 NULL,
    `PurchaseInvoiceId` int NULL,
    `DepreciationMethod` longtext CHARACTER SET utf8mb4 NOT NULL,
    `UsefulLifeYears` int NOT NULL,
    `SalvageValue` decimal(18,2) NOT NULL,
    `DepreciationStartDate` datetime(6) NULL,
    `AccumulatedDepreciation` decimal(18,2) NOT NULL,
    `Status` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Location` longtext CHARACTER SET utf8mb4 NULL,
    `SerialNumber` longtext CHARACTER SET utf8mb4 NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentUrl` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentPublicId` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `AssetAccountId` int NULL,
    `AccumDepreciationAccountId` int NULL,
    `DepreciationExpenseAccountId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_FixedAssets` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_FixedAssets_Accounts_AccumDepreciationAccountId` FOREIGN KEY (`AccumDepreciationAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_FixedAssets_Accounts_AssetAccountId` FOREIGN KEY (`AssetAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_FixedAssets_Accounts_DepreciationExpenseAccountId` FOREIGN KEY (`DepreciationExpenseAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_FixedAssets_FixedAssetCategories_CategoryId` FOREIGN KEY (`CategoryId`) REFERENCES `FixedAssetCategories` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_FixedAssets_PurchaseInvoices_PurchaseInvoiceId` FOREIGN KEY (`PurchaseInvoiceId`) REFERENCES `PurchaseInvoices` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4;

CREATE TABLE `AssetDepreciations` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `DepreciationNumber` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `FixedAssetId` int NOT NULL,
    `DepreciationDate` datetime(6) NOT NULL,
    `PeriodYear` int NOT NULL,
    `PeriodMonth` int NOT NULL,
    `DepreciationAmount` decimal(18,2) NOT NULL,
    `AccumulatedBefore` decimal(18,2) NOT NULL,
    `AccumulatedAfter` decimal(18,2) NOT NULL,
    `BookValueAfter` decimal(18,2) NOT NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `JournalEntryId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_AssetDepreciations` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_AssetDepreciations_FixedAssets_FixedAssetId` FOREIGN KEY (`FixedAssetId`) REFERENCES `FixedAssets` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_AssetDepreciations_JournalEntries_JournalEntryId` FOREIGN KEY (`JournalEntryId`) REFERENCES `JournalEntries` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4;

CREATE TABLE `AssetDisposals` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `DisposalNumber` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `FixedAssetId` int NOT NULL,
    `DisposalType` longtext CHARACTER SET utf8mb4 NOT NULL,
    `DisposalDate` datetime(6) NOT NULL,
    `BookValueAtDisposal` decimal(18,2) NOT NULL,
    `AccumulatedAtDisposal` decimal(18,2) NOT NULL,
    `SaleProceeds` decimal(18,2) NOT NULL,
    `ProceedsAccountId` int NULL,
    `GainAccountId` int NULL,
    `LossAccountId` int NULL,
    `Buyer` longtext CHARACTER SET utf8mb4 NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentUrl` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentPublicId` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `JournalEntryId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_AssetDisposals` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_AssetDisposals_Accounts_GainAccountId` FOREIGN KEY (`GainAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_AssetDisposals_Accounts_LossAccountId` FOREIGN KEY (`LossAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_AssetDisposals_Accounts_ProceedsAccountId` FOREIGN KEY (`ProceedsAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_AssetDisposals_FixedAssets_FixedAssetId` FOREIGN KEY (`FixedAssetId`) REFERENCES `FixedAssets` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_AssetDisposals_JournalEntries_JournalEntryId` FOREIGN KEY (`JournalEntryId`) REFERENCES `JournalEntries` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4;

CREATE UNIQUE INDEX `IX_AssetDepreciations_DepreciationNumber` ON `AssetDepreciations` (`DepreciationNumber`);

CREATE INDEX `IX_AssetDepreciations_FixedAssetId` ON `AssetDepreciations` (`FixedAssetId`);

CREATE INDEX `IX_AssetDepreciations_JournalEntryId` ON `AssetDepreciations` (`JournalEntryId`);

CREATE UNIQUE INDEX `IX_AssetDisposals_DisposalNumber` ON `AssetDisposals` (`DisposalNumber`);

CREATE INDEX `IX_AssetDisposals_FixedAssetId` ON `AssetDisposals` (`FixedAssetId`);

CREATE INDEX `IX_AssetDisposals_GainAccountId` ON `AssetDisposals` (`GainAccountId`);

CREATE INDEX `IX_AssetDisposals_JournalEntryId` ON `AssetDisposals` (`JournalEntryId`);

CREATE INDEX `IX_AssetDisposals_LossAccountId` ON `AssetDisposals` (`LossAccountId`);

CREATE INDEX `IX_AssetDisposals_ProceedsAccountId` ON `AssetDisposals` (`ProceedsAccountId`);

CREATE INDEX `IX_FixedAssetCategories_AccumDepreciationAccountId` ON `FixedAssetCategories` (`AccumDepreciationAccountId`);

CREATE INDEX `IX_FixedAssetCategories_AssetAccountId` ON `FixedAssetCategories` (`AssetAccountId`);

CREATE INDEX `IX_FixedAssetCategories_DepreciationExpenseAccountId` ON `FixedAssetCategories` (`DepreciationExpenseAccountId`);

CREATE INDEX `IX_FixedAssets_AccumDepreciationAccountId` ON `FixedAssets` (`AccumDepreciationAccountId`);

CREATE INDEX `IX_FixedAssets_AssetAccountId` ON `FixedAssets` (`AssetAccountId`);

CREATE UNIQUE INDEX `IX_FixedAssets_AssetNumber` ON `FixedAssets` (`AssetNumber`);

CREATE INDEX `IX_FixedAssets_CategoryId` ON `FixedAssets` (`CategoryId`);

CREATE INDEX `IX_FixedAssets_DepreciationExpenseAccountId` ON `FixedAssets` (`DepreciationExpenseAccountId`);

CREATE INDEX `IX_FixedAssets_PurchaseInvoiceId` ON `FixedAssets` (`PurchaseInvoiceId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260417074956_AddFixedAssets', '9.0.0');

ALTER TABLE `JournalLines` ADD `EmployeeId` int NULL;

CREATE TABLE `Employees` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmployeeNumber` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `Name` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Phone` longtext CHARACTER SET utf8mb4 NULL,
    `Email` longtext CHARACTER SET utf8mb4 NULL,
    `NationalId` longtext CHARACTER SET utf8mb4 NULL,
    `JobTitle` longtext CHARACTER SET utf8mb4 NULL,
    `Department` longtext CHARACTER SET utf8mb4 NULL,
    `HireDate` datetime(6) NOT NULL,
    `TerminationDate` datetime(6) NULL,
    `BaseSalary` decimal(18,2) NOT NULL,
    `BankAccount` longtext CHARACTER SET utf8mb4 NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentUrl` longtext CHARACTER SET utf8mb4 NULL,
    `AttachmentPublicId` longtext CHARACTER SET utf8mb4 NULL,
    `Status` longtext CHARACTER SET utf8mb4 NOT NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `AccountId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_Employees` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Employees_Accounts_AccountId` FOREIGN KEY (`AccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4;

CREATE TABLE `PayrollRuns` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `PayrollNumber` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `PeriodYear` int NOT NULL,
    `PeriodMonth` int NOT NULL,
    `TotalBasicSalary` decimal(18,2) NOT NULL,
    `TotalBonuses` decimal(18,2) NOT NULL,
    `TotalDeductions` decimal(18,2) NOT NULL,
    `TotalAdvancesDeducted` decimal(18,2) NOT NULL,
    `TotalNetPayable` decimal(18,2) NOT NULL,
    `Status` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `WagesExpenseAccountId` int NULL,
    `AccruedSalariesAccountId` int NULL,
    `DeductionRevenueAccountId` int NULL,
    `AdvancesAccountId` int NULL,
    `JournalEntryId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_PayrollRuns` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_PayrollRuns_Accounts_AccruedSalariesAccountId` FOREIGN KEY (`AccruedSalariesAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_PayrollRuns_Accounts_AdvancesAccountId` FOREIGN KEY (`AdvancesAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_PayrollRuns_Accounts_DeductionRevenueAccountId` FOREIGN KEY (`DeductionRevenueAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_PayrollRuns_Accounts_WagesExpenseAccountId` FOREIGN KEY (`WagesExpenseAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_PayrollRuns_JournalEntries_JournalEntryId` FOREIGN KEY (`JournalEntryId`) REFERENCES `JournalEntries` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4;

CREATE TABLE `EmployeeAdvances` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `AdvanceNumber` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `EmployeeId` int NOT NULL,
    `AdvanceDate` datetime(6) NOT NULL,
    `Amount` decimal(18,2) NOT NULL,
    `DeductedAmount` decimal(18,2) NOT NULL,
    `Status` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Reason` longtext CHARACTER SET utf8mb4 NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `CashAccountId` int NULL,
    `JournalEntryId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_EmployeeAdvances` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EmployeeAdvances_Accounts_CashAccountId` FOREIGN KEY (`CashAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_EmployeeAdvances_Employees_EmployeeId` FOREIGN KEY (`EmployeeId`) REFERENCES `Employees` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_EmployeeAdvances_JournalEntries_JournalEntryId` FOREIGN KEY (`JournalEntryId`) REFERENCES `JournalEntries` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4;

CREATE TABLE `EmployeeBonuses` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `BonusNumber` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `EmployeeId` int NOT NULL,
    `BonusDate` datetime(6) NOT NULL,
    `Amount` decimal(18,2) NOT NULL,
    `BonusType` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Reason` longtext CHARACTER SET utf8mb4 NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `PayrollRunId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_EmployeeBonuses` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EmployeeBonuses_Employees_EmployeeId` FOREIGN KEY (`EmployeeId`) REFERENCES `Employees` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_EmployeeBonuses_PayrollRuns_PayrollRunId` FOREIGN KEY (`PayrollRunId`) REFERENCES `PayrollRuns` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4;

CREATE TABLE `EmployeeDeductions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `DeductionNumber` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `EmployeeId` int NOT NULL,
    `DeductionDate` datetime(6) NOT NULL,
    `Amount` decimal(18,2) NOT NULL,
    `DeductionType` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Reason` longtext CHARACTER SET utf8mb4 NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `PayrollRunId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_EmployeeDeductions` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EmployeeDeductions_Employees_EmployeeId` FOREIGN KEY (`EmployeeId`) REFERENCES `Employees` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_EmployeeDeductions_PayrollRuns_PayrollRunId` FOREIGN KEY (`PayrollRunId`) REFERENCES `PayrollRuns` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4;

CREATE TABLE `PayrollItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `PayrollRunId` int NOT NULL,
    `EmployeeId` int NOT NULL,
    `BasicSalary` decimal(18,2) NOT NULL,
    `BonusAmount` decimal(18,2) NOT NULL,
    `DeductionAmount` decimal(18,2) NOT NULL,
    `AdvanceDeducted` decimal(18,2) NOT NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_PayrollItems` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_PayrollItems_Employees_EmployeeId` FOREIGN KEY (`EmployeeId`) REFERENCES `Employees` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_PayrollItems_PayrollRuns_PayrollRunId` FOREIGN KEY (`PayrollRunId`) REFERENCES `PayrollRuns` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_JournalLines_EmployeeId` ON `JournalLines` (`EmployeeId`);

CREATE UNIQUE INDEX `IX_EmployeeAdvances_AdvanceNumber` ON `EmployeeAdvances` (`AdvanceNumber`);

CREATE INDEX `IX_EmployeeAdvances_CashAccountId` ON `EmployeeAdvances` (`CashAccountId`);

CREATE INDEX `IX_EmployeeAdvances_EmployeeId` ON `EmployeeAdvances` (`EmployeeId`);

CREATE INDEX `IX_EmployeeAdvances_JournalEntryId` ON `EmployeeAdvances` (`JournalEntryId`);

CREATE UNIQUE INDEX `IX_EmployeeBonuses_BonusNumber` ON `EmployeeBonuses` (`BonusNumber`);

CREATE INDEX `IX_EmployeeBonuses_EmployeeId` ON `EmployeeBonuses` (`EmployeeId`);

CREATE INDEX `IX_EmployeeBonuses_PayrollRunId` ON `EmployeeBonuses` (`PayrollRunId`);

CREATE UNIQUE INDEX `IX_EmployeeDeductions_DeductionNumber` ON `EmployeeDeductions` (`DeductionNumber`);

CREATE INDEX `IX_EmployeeDeductions_EmployeeId` ON `EmployeeDeductions` (`EmployeeId`);

CREATE INDEX `IX_EmployeeDeductions_PayrollRunId` ON `EmployeeDeductions` (`PayrollRunId`);

CREATE INDEX `IX_Employees_AccountId` ON `Employees` (`AccountId`);

CREATE UNIQUE INDEX `IX_Employees_EmployeeNumber` ON `Employees` (`EmployeeNumber`);

CREATE INDEX `IX_PayrollItems_EmployeeId` ON `PayrollItems` (`EmployeeId`);

CREATE INDEX `IX_PayrollItems_PayrollRunId` ON `PayrollItems` (`PayrollRunId`);

CREATE INDEX `IX_PayrollRuns_AccruedSalariesAccountId` ON `PayrollRuns` (`AccruedSalariesAccountId`);

CREATE INDEX `IX_PayrollRuns_AdvancesAccountId` ON `PayrollRuns` (`AdvancesAccountId`);

CREATE INDEX `IX_PayrollRuns_DeductionRevenueAccountId` ON `PayrollRuns` (`DeductionRevenueAccountId`);

CREATE INDEX `IX_PayrollRuns_JournalEntryId` ON `PayrollRuns` (`JournalEntryId`);

CREATE UNIQUE INDEX `IX_PayrollRuns_PayrollNumber` ON `PayrollRuns` (`PayrollNumber`);

CREATE INDEX `IX_PayrollRuns_WagesExpenseAccountId` ON `PayrollRuns` (`WagesExpenseAccountId`);

ALTER TABLE `JournalLines` ADD CONSTRAINT `FK_JournalLines_Employees_EmployeeId` FOREIGN KEY (`EmployeeId`) REFERENCES `Employees` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260417080042_AddHRPayroll', '9.0.0');

ALTER TABLE `ProductDiscounts` DROP FOREIGN KEY `FK_ProductDiscounts_Products_ProductId`;

ALTER TABLE `ProductDiscounts` MODIFY COLUMN `ProductId` int NULL;

ALTER TABLE `ProductDiscounts` ADD `BrandId` int NULL;

ALTER TABLE `ProductDiscounts` ADD `CategoryId` int NULL;

ALTER TABLE `OrderItems` ADD `DiscountAmount` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `OrderItems` ADD `OriginalUnitPrice` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `AspNetUsers` ADD `RefreshToken` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `AspNetUsers` ADD `RefreshTokenExpiry` datetime(6) NULL;

CREATE INDEX `IX_ProductDiscounts_BrandId` ON `ProductDiscounts` (`BrandId`);

CREATE INDEX `IX_ProductDiscounts_CategoryId` ON `ProductDiscounts` (`CategoryId`);

ALTER TABLE `ProductDiscounts` ADD CONSTRAINT `FK_ProductDiscounts_Brands_BrandId` FOREIGN KEY (`BrandId`) REFERENCES `Brands` (`Id`);

ALTER TABLE `ProductDiscounts` ADD CONSTRAINT `FK_ProductDiscounts_Categories_CategoryId` FOREIGN KEY (`CategoryId`) REFERENCES `Categories` (`Id`);

ALTER TABLE `ProductDiscounts` ADD CONSTRAINT `FK_ProductDiscounts_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260417081925_AddRefreshToken', '9.0.0');

ALTER TABLE `StoreSettings` ADD `AccountingLockDate` datetime(6) NULL;

UPDATE `StoreSettings` SET `AccountingLockDate` = NULL
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260417120113_AddAccountingLockDate', '9.0.0');

ALTER TABLE `Employees` ADD `AppUserId` varchar(255) CHARACTER SET utf8mb4 NULL;

CREATE UNIQUE INDEX `IX_Employees_AppUserId` ON `Employees` (`AppUserId`);

ALTER TABLE `Employees` ADD CONSTRAINT `FK_Employees_AspNetUsers_AppUserId` FOREIGN KEY (`AppUserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260417122357_LinkEmployeeToAppUser', '9.0.0');

ALTER TABLE `Employees` DROP COLUMN `Department`;

ALTER TABLE `Suppliers` ADD `OpeningBalance` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `ReceiptVouchers` ADD `CostCenter` int NULL;

ALTER TABLE `PaymentVouchers` ADD `CostCenter` int NULL;

ALTER TABLE `JournalLines` ADD `CostCenter` int NULL;

ALTER TABLE `JournalEntries` ADD `CostCenter` int NULL;

ALTER TABLE `Employees` ADD `DepartmentId` int NULL;

ALTER TABLE `Employees` ADD `FixedAllowance` decimal(18,2) NOT NULL DEFAULT 0.0;

ALTER TABLE `Employees` ADD `FixedDeduction` decimal(18,2) NOT NULL DEFAULT 0.0;

ALTER TABLE `EmployeeDeductions` ADD `CashAccountId` int NULL;

ALTER TABLE `EmployeeDeductions` ADD `JournalEntryId` int NULL;

ALTER TABLE `EmployeeBonuses` ADD `CashAccountId` int NULL;

ALTER TABLE `EmployeeBonuses` ADD `JournalEntryId` int NULL;

CREATE TABLE `Departments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_Departments` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_PurchaseInvoices_InvoiceDate` ON `PurchaseInvoices` (`InvoiceDate`);

CREATE INDEX `IX_Orders_CreatedAt` ON `Orders` (`CreatedAt`);

CREATE INDEX `IX_JournalEntries_EntryDate` ON `JournalEntries` (`EntryDate`);

CREATE INDEX `IX_Employees_DepartmentId` ON `Employees` (`DepartmentId`);

CREATE INDEX `IX_EmployeeDeductions_CashAccountId` ON `EmployeeDeductions` (`CashAccountId`);

CREATE INDEX `IX_EmployeeDeductions_JournalEntryId` ON `EmployeeDeductions` (`JournalEntryId`);

CREATE INDEX `IX_EmployeeBonuses_CashAccountId` ON `EmployeeBonuses` (`CashAccountId`);

CREATE INDEX `IX_EmployeeBonuses_JournalEntryId` ON `EmployeeBonuses` (`JournalEntryId`);

ALTER TABLE `EmployeeBonuses` ADD CONSTRAINT `FK_EmployeeBonuses_Accounts_CashAccountId` FOREIGN KEY (`CashAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL;

ALTER TABLE `EmployeeBonuses` ADD CONSTRAINT `FK_EmployeeBonuses_JournalEntries_JournalEntryId` FOREIGN KEY (`JournalEntryId`) REFERENCES `JournalEntries` (`Id`) ON DELETE SET NULL;

ALTER TABLE `EmployeeDeductions` ADD CONSTRAINT `FK_EmployeeDeductions_Accounts_CashAccountId` FOREIGN KEY (`CashAccountId`) REFERENCES `Accounts` (`Id`) ON DELETE SET NULL;

ALTER TABLE `EmployeeDeductions` ADD CONSTRAINT `FK_EmployeeDeductions_JournalEntries_JournalEntryId` FOREIGN KEY (`JournalEntryId`) REFERENCES `JournalEntries` (`Id`) ON DELETE SET NULL;

ALTER TABLE `Employees` ADD CONSTRAINT `FK_Employees_Departments_DepartmentId` FOREIGN KEY (`DepartmentId`) REFERENCES `Departments` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260419003833_AddReportIndexes', '9.0.0');

ALTER TABLE `PurchaseReturns` DROP FOREIGN KEY `FK_PurchaseReturns_PurchaseInvoices_PurchaseInvoiceId`;

ALTER TABLE `PurchaseReturns` MODIFY COLUMN `PurchaseInvoiceId` int NULL;

ALTER TABLE `PurchaseReturns` ADD `CashAccountId` int NULL;

ALTER TABLE `PurchaseReturns` ADD `PaymentTerms` int NOT NULL DEFAULT 0;

ALTER TABLE `PurchaseReturnItems` MODIFY COLUMN `PurchaseInvoiceItemId` int NULL;

CREATE INDEX `IX_PurchaseReturns_CashAccountId` ON `PurchaseReturns` (`CashAccountId`);

ALTER TABLE `PurchaseReturns` ADD CONSTRAINT `FK_PurchaseReturns_Accounts_CashAccountId` FOREIGN KEY (`CashAccountId`) REFERENCES `Accounts` (`Id`);

ALTER TABLE `PurchaseReturns` ADD CONSTRAINT `FK_PurchaseReturns_PurchaseInvoices_PurchaseInvoiceId` FOREIGN KEY (`PurchaseInvoiceId`) REFERENCES `PurchaseInvoices` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260419214214_SupportStandaloneReturns', '9.0.0');

ALTER TABLE `PurchaseReturnItems` ADD `Unit` longtext CHARACTER SET utf8mb4 NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260419221832_AddUnitToPurchaseReturnItems', '9.0.0');

ALTER TABLE `FixedAssets` ADD `CostCenter` int NULL;

ALTER TABLE `FixedAssetCategories` ADD `CostCenter` int NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260420030100_HR_Refinements_And_Fixes', '9.0.0');

ALTER TABLE `Orders` ADD `TemporalDiscount` decimal(65,30) NOT NULL DEFAULT 0.0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260420083557_AddTemporalDiscountToOrder', '9.0.0');

ALTER TABLE `ProductDiscounts` ADD `ApplyTo` int NOT NULL DEFAULT 0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260420083729_UpdateProductDiscountApplyTo', '9.0.0');

ALTER TABLE `ReceiptVouchers` ADD `EmployeeId` int NULL;

ALTER TABLE `PurchaseReturns` ADD `CostCenter` int NULL;

ALTER TABLE `PurchaseInvoices` ADD `CostCenter` int NULL;

ALTER TABLE `PaymentVouchers` ADD `EmployeeId` int NULL;

CREATE INDEX `IX_ReceiptVouchers_EmployeeId` ON `ReceiptVouchers` (`EmployeeId`);

CREATE INDEX `IX_PaymentVouchers_EmployeeId` ON `PaymentVouchers` (`EmployeeId`);

ALTER TABLE `PaymentVouchers` ADD CONSTRAINT `FK_PaymentVouchers_Employees_EmployeeId` FOREIGN KEY (`EmployeeId`) REFERENCES `Employees` (`Id`);

ALTER TABLE `ReceiptVouchers` ADD CONSTRAINT `FK_ReceiptVouchers_Employees_EmployeeId` FOREIGN KEY (`EmployeeId`) REFERENCES `Employees` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260420204601_AddEmployeeToVouchers', '9.0.0');

ALTER TABLE `Accounts` ADD `CanReceivePayment` tinyint(1) NOT NULL DEFAULT FALSE;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260421144751_AddCanReceivePaymentToAccount', '9.0.0');

CREATE TABLE `InventoryAudits` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Title` longtext CHARACTER SET utf8mb4 NOT NULL,
    `AuditDate` datetime(6) NOT NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `Status` int NOT NULL,
    `TotalExpectedValue` decimal(18,2) NOT NULL,
    `TotalActualValue` decimal(18,2) NOT NULL,
    `JournalEntryId` int NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_InventoryAudits` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `InventoryAuditItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `InventoryAuditId` int NOT NULL,
    `ProductId` int NULL,
    `ProductVariantId` int NULL,
    `ExpectedQuantity` int NOT NULL,
    `ActualQuantity` int NOT NULL,
    `UnitCost` decimal(18,2) NOT NULL,
    `Note` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_InventoryAuditItems` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_InventoryAuditItems_InventoryAudits_InventoryAuditId` FOREIGN KEY (`InventoryAuditId`) REFERENCES `InventoryAudits` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_InventoryAuditItems_InventoryAuditId` ON `InventoryAuditItems` (`InventoryAuditId`);

CREATE INDEX `IX_InventoryAudits_JournalEntryId` ON `InventoryAudits` (`JournalEntryId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260421210104_AddInventoryAuditsFinal', '9.0.0');

ALTER TABLE `SupplierPayments` MODIFY COLUMN `CostCenter` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `PurchaseReturns` MODIFY COLUMN `CostCenter` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `PurchaseInvoices` MODIFY COLUMN `CostCenter` longtext CHARACTER SET utf8mb4 NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260422083256_AddCostCenterToPurchasesFinal', '9.0.0');

ALTER TABLE `StoreSettings` ADD `AutoPrintReceipt` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `BarcodeShowColor` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `BarcodeShowName` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `BarcodeShowPrice` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `BarcodeShowSize` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `FacebookPixelId` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `GoogleAnalyticsId` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `OrderSuccessMessageAr` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `OrderSuccessMessageEn` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `ReceiptExtraCopies` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `SiteKeywords` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `SiteMetaDescriptionAr` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `SiteMetaDescriptionEn` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `WhatsAppOrderTemplate` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `WhatsAppReturnTemplate` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `WhatsAppShippingTemplate` longtext CHARACTER SET utf8mb4 NULL;

UPDATE `StoreSettings` SET `AutoPrintReceipt` = FALSE, `BarcodeShowColor` = TRUE, `BarcodeShowName` = TRUE, `BarcodeShowPrice` = TRUE, `BarcodeShowSize` = TRUE, `FacebookPixelId` = NULL, `GoogleAnalyticsId` = NULL, `OrderSuccessMessageAr` = 'شكراً لتسوقك معنا! سيقوم فريقنا بالتواصل معك قريباً لتأكيد الطلب.', `OrderSuccessMessageEn` = 'Thank you for shopping with us! Our team will contact you soon to confirm your order.', `ReceiptExtraCopies` = 0, `SiteKeywords` = NULL, `SiteMetaDescriptionAr` = NULL, `SiteMetaDescriptionEn` = NULL, `WhatsAppOrderTemplate` = 'أهلاً {customerName}، تم استلام طلبك رقم #{orderNumber} وجاري التجهيز.', `WhatsAppReturnTemplate` = 'تم استلام طلب المرتجع الخاص بك رقم #{orderNumber}، وجاري مراجعته.', `WhatsAppShippingTemplate` = 'طلبك #{orderNumber} في الطريق مع المندوب، سيصلك قريباً.'
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260426070840_AddStoreSettingsFinal', '9.0.0');

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260426071100_StoreSettings_Messaging_SEO', '9.0.0');

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260426074153_StoreSettings_Latest_Updates', '9.0.0');

ALTER TABLE `StoreSettings` ADD `OrderStatusAfterPrint` varchar(50) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `QzA4Printer` varchar(100) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `QzBarcodePrinter` varchar(100) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `QzReceiptPrinter` varchar(100) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `ReceiptFontFamily` varchar(50) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';

ALTER TABLE `StoreSettings` ADD `ReceiptFontSize` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `ReceiptLogoPosition` varchar(20) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';

ALTER TABLE `StoreSettings` ADD `ReceiptLogoWidth` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `ReceiptPaperSize` varchar(20) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';

ALTER TABLE `StoreSettings` ADD `ReceiptWidth` int NOT NULL DEFAULT 0;

UPDATE `StoreSettings` SET `OrderStatusAfterPrint` = NULL, `QzA4Printer` = NULL, `QzBarcodePrinter` = NULL, `QzReceiptPrinter` = NULL, `ReceiptFontFamily` = 'Alexandria', `ReceiptFontSize` = 11, `ReceiptLogoPosition` = 'center', `ReceiptLogoWidth` = 80, `ReceiptPaperSize` = 'Receipt', `ReceiptWidth` = 80
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260426121305_AddReceiptLogoSettings', '9.0.0');

ALTER TABLE `StoreSettings` ADD `ReceiptBarcodeHeight` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `ReceiptDensity` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `ReceiptLineStyle` varchar(10) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';

UPDATE `StoreSettings` SET `ReceiptBarcodeHeight` = 10, `ReceiptDensity` = 2, `ReceiptLineStyle` = 'dashed'
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260426121831_AddMoreReceiptControls', '9.0.0');

ALTER TABLE `StoreSettings` ADD `ReceiptSectionsOrder` varchar(1000) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';

UPDATE `StoreSettings` SET `ReceiptSectionsOrder` = 'header,order_info,items_table,totals_area,tafqeet,payment_info,footer_text,terms_conditions,barcode'
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260426123003_AddReceiptSectionsOrder', '9.0.0');

ALTER TABLE `Customers` ADD `CategoryId` int NULL;

CREATE TABLE `CustomerCategories` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `NameAr` longtext CHARACTER SET utf8mb4 NOT NULL,
    `NameEn` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `DefaultDiscount` decimal(18,2) NOT NULL,
    `MinimumSpending` decimal(65,30) NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_CustomerCategories` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_Customers_CategoryId` ON `Customers` (`CategoryId`);

ALTER TABLE `Customers` ADD CONSTRAINT `FK_Customers_CustomerCategories_CategoryId` FOREIGN KEY (`CategoryId`) REFERENCES `CustomerCategories` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260427121704_UpdateCustomerCategoryThresholds', '9.0.0');

ALTER TABLE `JournalEntries` MODIFY COLUMN `Type` varchar(255) CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `JournalEntries` MODIFY COLUMN `Reference` varchar(255) CHARACTER SET utf8mb4 NULL;

CREATE UNIQUE INDEX `IX_JournalEntries_Reference_Type` ON `JournalEntries` (`Reference`, `Type`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260428020759_FinalModernization', '9.0.0');

ALTER TABLE `AssetDepreciations` DROP FOREIGN KEY `FK_AssetDepreciations_JournalEntries_JournalEntryId`;

ALTER TABLE `AssetDisposals` DROP FOREIGN KEY `FK_AssetDisposals_Accounts_GainAccountId`;

ALTER TABLE `AssetDisposals` DROP FOREIGN KEY `FK_AssetDisposals_Accounts_LossAccountId`;

ALTER TABLE `AssetDisposals` DROP FOREIGN KEY `FK_AssetDisposals_Accounts_ProceedsAccountId`;

ALTER TABLE `AssetDisposals` DROP FOREIGN KEY `FK_AssetDisposals_JournalEntries_JournalEntryId`;

ALTER TABLE `EmployeeAdvances` DROP FOREIGN KEY `FK_EmployeeAdvances_Accounts_CashAccountId`;

ALTER TABLE `EmployeeAdvances` DROP FOREIGN KEY `FK_EmployeeAdvances_JournalEntries_JournalEntryId`;

ALTER TABLE `EmployeeBonuses` DROP FOREIGN KEY `FK_EmployeeBonuses_Accounts_CashAccountId`;

ALTER TABLE `EmployeeBonuses` DROP FOREIGN KEY `FK_EmployeeBonuses_JournalEntries_JournalEntryId`;

ALTER TABLE `EmployeeBonuses` DROP FOREIGN KEY `FK_EmployeeBonuses_PayrollRuns_PayrollRunId`;

ALTER TABLE `EmployeeDeductions` DROP FOREIGN KEY `FK_EmployeeDeductions_Accounts_CashAccountId`;

ALTER TABLE `EmployeeDeductions` DROP FOREIGN KEY `FK_EmployeeDeductions_JournalEntries_JournalEntryId`;

ALTER TABLE `EmployeeDeductions` DROP FOREIGN KEY `FK_EmployeeDeductions_PayrollRuns_PayrollRunId`;

ALTER TABLE `FixedAssetCategories` DROP FOREIGN KEY `FK_FixedAssetCategories_Accounts_AccumDepreciationAccountId`;

ALTER TABLE `FixedAssetCategories` DROP FOREIGN KEY `FK_FixedAssetCategories_Accounts_AssetAccountId`;

ALTER TABLE `FixedAssetCategories` DROP FOREIGN KEY `FK_FixedAssetCategories_Accounts_DepreciationExpenseAccountId`;

ALTER TABLE `FixedAssets` DROP FOREIGN KEY `FK_FixedAssets_Accounts_AccumDepreciationAccountId`;

ALTER TABLE `FixedAssets` DROP FOREIGN KEY `FK_FixedAssets_Accounts_AssetAccountId`;

ALTER TABLE `FixedAssets` DROP FOREIGN KEY `FK_FixedAssets_Accounts_DepreciationExpenseAccountId`;

ALTER TABLE `FixedAssets` DROP FOREIGN KEY `FK_FixedAssets_PurchaseInvoices_PurchaseInvoiceId`;

ALTER TABLE `PayrollRuns` DROP FOREIGN KEY `FK_PayrollRuns_Accounts_AccruedSalariesAccountId`;

ALTER TABLE `PayrollRuns` DROP FOREIGN KEY `FK_PayrollRuns_Accounts_AdvancesAccountId`;

ALTER TABLE `PayrollRuns` DROP FOREIGN KEY `FK_PayrollRuns_Accounts_DeductionRevenueAccountId`;

ALTER TABLE `PayrollRuns` DROP FOREIGN KEY `FK_PayrollRuns_Accounts_WagesExpenseAccountId`;

ALTER TABLE `PayrollRuns` ADD `TotalCommunication` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `PayrollRuns` ADD `TotalTransportation` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `PayrollItems` ADD `CommunicationAllowance` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `PayrollItems` ADD `TransportationAllowance` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `Employees` ADD `BonusAmount` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `Employees` ADD `CommunicationAllowance` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `Employees` ADD `TransportationAllowance` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `Categories` ADD `SizeGroupId` int NULL;

CREATE TABLE `SizeGroups` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_SizeGroups` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `SizeValues` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `SizeGroupId` int NOT NULL,
    `Value` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
    `SortOrder` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_SizeValues` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_SizeValues_SizeGroups_SizeGroupId` FOREIGN KEY (`SizeGroupId`) REFERENCES `SizeGroups` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_Categories_SizeGroupId` ON `Categories` (`SizeGroupId`);

CREATE INDEX `IX_SizeValues_SizeGroupId` ON `SizeValues` (`SizeGroupId`);

ALTER TABLE `AssetDepreciations` ADD CONSTRAINT `FK_AssetDepreciations_JournalEntries_JournalEntryId` FOREIGN KEY (`JournalEntryId`) REFERENCES `JournalEntries` (`Id`);

ALTER TABLE `AssetDisposals` ADD CONSTRAINT `FK_AssetDisposals_Accounts_GainAccountId` FOREIGN KEY (`GainAccountId`) REFERENCES `Accounts` (`Id`);

ALTER TABLE `AssetDisposals` ADD CONSTRAINT `FK_AssetDisposals_Accounts_LossAccountId` FOREIGN KEY (`LossAccountId`) REFERENCES `Accounts` (`Id`);

ALTER TABLE `AssetDisposals` ADD CONSTRAINT `FK_AssetDisposals_Accounts_ProceedsAccountId` FOREIGN KEY (`ProceedsAccountId`) REFERENCES `Accounts` (`Id`);

ALTER TABLE `AssetDisposals` ADD CONSTRAINT `FK_AssetDisposals_JournalEntries_JournalEntryId` FOREIGN KEY (`JournalEntryId`) REFERENCES `JournalEntries` (`Id`);

ALTER TABLE `Categories` ADD CONSTRAINT `FK_Categories_SizeGroups_SizeGroupId` FOREIGN KEY (`SizeGroupId`) REFERENCES `SizeGroups` (`Id`) ON DELETE SET NULL;

ALTER TABLE `EmployeeAdvances` ADD CONSTRAINT `FK_EmployeeAdvances_Accounts_CashAccountId` FOREIGN KEY (`CashAccountId`) REFERENCES `Accounts` (`Id`);

ALTER TABLE `EmployeeAdvances` ADD CONSTRAINT `FK_EmployeeAdvances_JournalEntries_JournalEntryId` FOREIGN KEY (`JournalEntryId`) REFERENCES `JournalEntries` (`Id`);

ALTER TABLE `EmployeeBonuses` ADD CONSTRAINT `FK_EmployeeBonuses_Accounts_CashAccountId` FOREIGN KEY (`CashAccountId`) REFERENCES `Accounts` (`Id`);

ALTER TABLE `EmployeeBonuses` ADD CONSTRAINT `FK_EmployeeBonuses_JournalEntries_JournalEntryId` FOREIGN KEY (`JournalEntryId`) REFERENCES `JournalEntries` (`Id`);

ALTER TABLE `EmployeeBonuses` ADD CONSTRAINT `FK_EmployeeBonuses_PayrollRuns_PayrollRunId` FOREIGN KEY (`PayrollRunId`) REFERENCES `PayrollRuns` (`Id`);

ALTER TABLE `EmployeeDeductions` ADD CONSTRAINT `FK_EmployeeDeductions_Accounts_CashAccountId` FOREIGN KEY (`CashAccountId`) REFERENCES `Accounts` (`Id`);

ALTER TABLE `EmployeeDeductions` ADD CONSTRAINT `FK_EmployeeDeductions_JournalEntries_JournalEntryId` FOREIGN KEY (`JournalEntryId`) REFERENCES `JournalEntries` (`Id`);

ALTER TABLE `EmployeeDeductions` ADD CONSTRAINT `FK_EmployeeDeductions_PayrollRuns_PayrollRunId` FOREIGN KEY (`PayrollRunId`) REFERENCES `PayrollRuns` (`Id`);

ALTER TABLE `FixedAssetCategories` ADD CONSTRAINT `FK_FixedAssetCategories_Accounts_AccumDepreciationAccountId` FOREIGN KEY (`AccumDepreciationAccountId`) REFERENCES `Accounts` (`Id`);

ALTER TABLE `FixedAssetCategories` ADD CONSTRAINT `FK_FixedAssetCategories_Accounts_AssetAccountId` FOREIGN KEY (`AssetAccountId`) REFERENCES `Accounts` (`Id`);

ALTER TABLE `FixedAssetCategories` ADD CONSTRAINT `FK_FixedAssetCategories_Accounts_DepreciationExpenseAccountId` FOREIGN KEY (`DepreciationExpenseAccountId`) REFERENCES `Accounts` (`Id`);

ALTER TABLE `FixedAssets` ADD CONSTRAINT `FK_FixedAssets_Accounts_AccumDepreciationAccountId` FOREIGN KEY (`AccumDepreciationAccountId`) REFERENCES `Accounts` (`Id`);

ALTER TABLE `FixedAssets` ADD CONSTRAINT `FK_FixedAssets_Accounts_AssetAccountId` FOREIGN KEY (`AssetAccountId`) REFERENCES `Accounts` (`Id`);

ALTER TABLE `FixedAssets` ADD CONSTRAINT `FK_FixedAssets_Accounts_DepreciationExpenseAccountId` FOREIGN KEY (`DepreciationExpenseAccountId`) REFERENCES `Accounts` (`Id`);

ALTER TABLE `FixedAssets` ADD CONSTRAINT `FK_FixedAssets_PurchaseInvoices_PurchaseInvoiceId` FOREIGN KEY (`PurchaseInvoiceId`) REFERENCES `PurchaseInvoices` (`Id`);

ALTER TABLE `PayrollRuns` ADD CONSTRAINT `FK_PayrollRuns_Accounts_AccruedSalariesAccountId` FOREIGN KEY (`AccruedSalariesAccountId`) REFERENCES `Accounts` (`Id`);

ALTER TABLE `PayrollRuns` ADD CONSTRAINT `FK_PayrollRuns_Accounts_AdvancesAccountId` FOREIGN KEY (`AdvancesAccountId`) REFERENCES `Accounts` (`Id`);

ALTER TABLE `PayrollRuns` ADD CONSTRAINT `FK_PayrollRuns_Accounts_DeductionRevenueAccountId` FOREIGN KEY (`DeductionRevenueAccountId`) REFERENCES `Accounts` (`Id`);

ALTER TABLE `PayrollRuns` ADD CONSTRAINT `FK_PayrollRuns_Accounts_WagesExpenseAccountId` FOREIGN KEY (`WagesExpenseAccountId`) REFERENCES `Accounts` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260428113804_AddSizeGroups', '9.0.0');

ALTER TABLE `Products` ADD `SizeGroupId` int NULL;

CREATE INDEX `IX_Products_SizeGroupId` ON `Products` (`SizeGroupId`);

ALTER TABLE `Products` ADD CONSTRAINT `FK_Products_SizeGroups_SizeGroupId` FOREIGN KEY (`SizeGroupId`) REFERENCES `SizeGroups` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260428114409_AddSizeGroupToProduct', '9.0.0');

ALTER TABLE `PayrollRuns` ADD `TotalFixedAllowances` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `PayrollItems` ADD `FixedAllowance` decimal(65,30) NOT NULL DEFAULT 0.0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260429004405_AddFixedAllowancesToPayroll', '9.0.0');

ALTER TABLE `InventoryOpeningBalances` ADD `CostCenter` int NULL;

ALTER TABLE `Employees` ADD `CostCenter` int NULL;

ALTER TABLE `EmployeeDeductions` ADD `CostCenter` int NULL;

ALTER TABLE `EmployeeBonuses` ADD `CostCenter` int NULL;

ALTER TABLE `EmployeeAdvances` ADD `CostCenter` int NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260429094039_AddGlobalCostCenters', '9.0.0');

ALTER TABLE `InventoryMovements` ADD `CostCenter` int NULL;

ALTER TABLE `InventoryAudits` ADD `CostCenter` int NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260429095133_AddCostCenterToInventoryMovements2', '9.0.0');

ALTER TABLE `SupplierPayments` MODIFY COLUMN `ReferenceNumber` varchar(255) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `ReceiptVouchers` MODIFY COLUMN `Reference` varchar(255) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `PaymentVouchers` MODIFY COLUMN `Reference` varchar(255) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `OrderPayments` MODIFY COLUMN `Reference` varchar(255) CHARACTER SET utf8mb4 NULL;

CREATE INDEX `IX_SupplierPayments_ReferenceNumber` ON `SupplierPayments` (`ReferenceNumber`);

CREATE INDEX `IX_ReceiptVouchers_Reference` ON `ReceiptVouchers` (`Reference`);

CREATE INDEX `IX_PaymentVouchers_Reference` ON `PaymentVouchers` (`Reference`);

CREATE INDEX `IX_OrderPayments_Reference` ON `OrderPayments` (`Reference`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260429175317_AddPaymentReferenceIndexes', '9.0.0');

CREATE INDEX `IX_Orders_CustomerId_CreatedAt` ON `Orders` (`CustomerId`, `CreatedAt`);

CREATE INDEX `IX_InventoryMovements_ProductId_CreatedAt` ON `InventoryMovements` (`ProductId`, `CreatedAt`);

ALTER TABLE `Orders` DROP INDEX `IX_Orders_CustomerId`;

ALTER TABLE `InventoryMovements` DROP INDEX `IX_InventoryMovements_ProductId`;

ALTER TABLE `AuditLogs` COMMENT 'Immutable audit trail — insert-only, never update/delete.';

ALTER TABLE `Products` MODIFY COLUMN `Status` varchar(255) CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `Products` MODIFY COLUMN `Slug` varchar(255) CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `Orders` MODIFY COLUMN `Status` varchar(255) CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `InventoryMovements` MODIFY COLUMN `Reference` varchar(255) CHARACTER SET utf8mb4 NULL;

CREATE INDEX `IX_Products_Slug` ON `Products` (`Slug`);

CREATE INDEX `IX_Products_Status_TotalStock` ON `Products` (`Status`, `TotalStock`);

CREATE INDEX `IX_Orders_Status_CreatedAt` ON `Orders` (`Status`, `CreatedAt`);

CREATE INDEX `IX_InventoryMovements_Reference` ON `InventoryMovements` (`Reference`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260429190217_AddPerformanceIndexes', '9.0.0');

ALTER TABLE `StoreSettings` ADD `BarcodeLabelHeight` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `BarcodeLabelWidth` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `BarcodeShowStoreName` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `BarcodeSvgHeight` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `BarcodeSvgWidth` int NOT NULL DEFAULT 0;

UPDATE `StoreSettings` SET `BarcodeLabelHeight` = 25, `BarcodeLabelWidth` = 40, `BarcodeShowStoreName` = TRUE, `BarcodeSvgHeight` = 50, `BarcodeSvgWidth` = 180
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260501094609_StoreSettings_BarcodeConfig', '9.0.0');

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260503140227_AdvancedPerformanceIndexes', '9.0.0');

ALTER TABLE `JournalEntries` MODIFY COLUMN `Status` varchar(255) CHARACTER SET utf8mb4 NOT NULL;

CREATE INDEX `IX_JournalEntries_Status_EntryDate` ON `JournalEntries` (`Status`, `EntryDate`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260503145751_CheckForPendingChanges', '9.0.0');

ALTER TABLE `Orders` DROP INDEX `IX_Orders_Status_CreatedAt`;

ALTER TABLE `OrderPayments` MODIFY COLUMN `Method` varchar(255) CHARACTER SET utf8mb4 NOT NULL;

CREATE TABLE `DailyStats` (
    `TenantId` int NOT NULL,
    `Date` datetime(6) NOT NULL,
    `Source` int NOT NULL,
    `TotalSales` decimal(18,2) NOT NULL,
    `OrdersCount` int NOT NULL,
    `TotalCollections` decimal(18,2) NOT NULL,
    `TotalExpenses` decimal(18,2) NOT NULL,
    `Profit` decimal(18,2) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_DailyStats` PRIMARY KEY (`TenantId`, `Date`, `Source`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `DbSequences` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Prefix` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `Stamp` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `LastValue` int NOT NULL,
    `LastUpdatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_DbSequences` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `OutboxMessages` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `MessageId` char(36) COLLATE ascii_general_ci NOT NULL,
    `TenantId` int NOT NULL,
    `EventType` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Payload` longtext CHARACTER SET utf8mb4 NOT NULL,
    `ProcessedAt` datetime(6) NULL,
    `Error` longtext CHARACTER SET utf8mb4 NULL,
    `RetryCount` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_OutboxMessages` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_ReceiptVouchers_VoucherDate_Amount` ON `ReceiptVouchers` (`VoucherDate`, `Amount`);

CREATE INDEX `IX_PaymentVouchers_VoucherDate_Amount` ON `PaymentVouchers` (`VoucherDate`, `Amount`);

CREATE INDEX `IX_Orders_CreatedAt_Status_TotalAmount` ON `Orders` (`CreatedAt`, `Status`, `TotalAmount`);

CREATE INDEX `IX_OrderPayments_CreatedAt_Method_Amount` ON `OrderPayments` (`CreatedAt`, `Method`, `Amount`);

CREATE UNIQUE INDEX `IX_DbSequences_Prefix_Stamp` ON `DbSequences` (`Prefix`, `Stamp`);

CREATE UNIQUE INDEX `IX_OutboxMessages_MessageId` ON `OutboxMessages` (`MessageId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260506154120_AddDbSequences', '9.0.0');

ALTER TABLE `PurchaseInvoices` ADD `IsTaxInclusive` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `PurchaseInvoiceItems` ADD `IsTaxInclusive` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `PurchaseInvoiceItems` ADD `TaxRate` decimal(65,30) NOT NULL DEFAULT 0.0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260509212924_AddIsTaxInclusiveToPurchases', '9.0.0');

ALTER TABLE `Products` ADD `ColorGroupId` int NULL;

ALTER TABLE `Categories` ADD `ColorGroupId` int NULL;

CREATE TABLE `ColorGroups` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_ColorGroups` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `ColorValues` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ColorGroupId` int NOT NULL,
    `Value` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
    `SortOrder` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_ColorValues` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ColorValues_ColorGroups_ColorGroupId` FOREIGN KEY (`ColorGroupId`) REFERENCES `ColorGroups` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_Products_ColorGroupId` ON `Products` (`ColorGroupId`);

CREATE INDEX `IX_Categories_ColorGroupId` ON `Categories` (`ColorGroupId`);

CREATE INDEX `IX_ColorValues_ColorGroupId` ON `ColorValues` (`ColorGroupId`);

ALTER TABLE `Categories` ADD CONSTRAINT `FK_Categories_ColorGroups_ColorGroupId` FOREIGN KEY (`ColorGroupId`) REFERENCES `ColorGroups` (`Id`) ON DELETE SET NULL;

ALTER TABLE `Products` ADD CONSTRAINT `FK_Products_ColorGroups_ColorGroupId` FOREIGN KEY (`ColorGroupId`) REFERENCES `ColorGroups` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260510003041_AddColorManagementSystem', '9.0.0');

ALTER TABLE `PurchaseInvoices` ADD `IsAssetPurchase` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `PurchaseInvoiceItems` ADD `AssetName` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `PurchaseInvoiceItems` ADD `CreatedAssetId` int NULL;

ALTER TABLE `PurchaseInvoiceItems` ADD `FixedAssetCategoryId` int NULL;

CREATE INDEX `IX_PurchaseInvoiceItems_CreatedAssetId` ON `PurchaseInvoiceItems` (`CreatedAssetId`);

CREATE INDEX `IX_PurchaseInvoiceItems_FixedAssetCategoryId` ON `PurchaseInvoiceItems` (`FixedAssetCategoryId`);

ALTER TABLE `PurchaseInvoiceItems` ADD CONSTRAINT `FK_PurchaseInvoiceItems_FixedAssetCategories_FixedAssetCategory~` FOREIGN KEY (`FixedAssetCategoryId`) REFERENCES `FixedAssetCategories` (`Id`);

ALTER TABLE `PurchaseInvoiceItems` ADD CONSTRAINT `FK_PurchaseInvoiceItems_FixedAssets_CreatedAssetId` FOREIGN KEY (`CreatedAssetId`) REFERENCES `FixedAssets` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260511012107_AddIsAssetPurchaseToPurchaseInvoices', '9.0.0');

ALTER TABLE `Products` ADD `IsTaxInclusive` tinyint(1) NOT NULL DEFAULT FALSE;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260511024155_AddIsTaxInclusiveToProduct', '9.0.0');

CREATE TABLE `SpecialOffers` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `ThresholdQuantity` int NOT NULL,
    `FreeQuantity` int NULL,
    `DiscountPercentage` decimal(18,2) NOT NULL,
    `IsFullDiscount` tinyint(1) NOT NULL,
    `ValidFrom` datetime(6) NOT NULL,
    `ValidTo` datetime(6) NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `ApplyTo` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_SpecialOffers` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260511111221_AddFreeQuantityToSpecialOffer', '9.0.0');

ALTER TABLE `SpecialOffers` ADD `EligibleBrandIds` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `SpecialOffers` ADD `EligibleCategoryIds` longtext CHARACTER SET utf8mb4 NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260511113300_AddFiltersToSpecialOffer', '9.0.0');

ALTER TABLE `PayrollRuns` ADD `PaymentJournalEntryId` int NULL;

CREATE INDEX `IX_PayrollRuns_PaymentJournalEntryId` ON `PayrollRuns` (`PaymentJournalEntryId`);

ALTER TABLE `PayrollRuns` ADD CONSTRAINT `FK_PayrollRuns_JournalEntries_PaymentJournalEntryId` FOREIGN KEY (`PaymentJournalEntryId`) REFERENCES `JournalEntries` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260511143839_AddPayrollPaymentTracking', '9.0.0');

ALTER TABLE `StoreSettings` ADD `DefaultLanguage` varchar(10) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';

ALTER TABLE `StoreSettings` ADD `EnableAccounting` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `EnableECommerce` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `EnableFixedAssets` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `EnableHR` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `EnablePOS` tinyint(1) NOT NULL DEFAULT FALSE;

UPDATE `StoreSettings` SET `DefaultLanguage` = 'ar', `EnableAccounting` = TRUE, `EnableECommerce` = TRUE, `EnableFixedAssets` = TRUE, `EnableHR` = TRUE, `EnablePOS` = TRUE
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260512122552_AddDefaultLanguageToStoreInfo', '9.0.0');

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260512123847_FixSettingsNamingAndAddLanguage', '9.0.0');

ALTER TABLE `StoreSettings` ADD `EnableHoverEffects` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `EnablePageTransitions` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `ShowHeroSection` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `UseGlassmorphism` tinyint(1) NOT NULL DEFAULT FALSE;

UPDATE `StoreSettings` SET `EnableHoverEffects` = TRUE, `EnablePageTransitions` = TRUE, `ShowHeroSection` = TRUE, `UseGlassmorphism` = FALSE
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260512125154_AddMoreAppearanceSettings', '9.0.0');

ALTER TABLE `PayrollRuns` ADD `TotalAbsenceDeduction` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `PayrollItems` ADD `AbsenceDays` int NOT NULL DEFAULT 0;

ALTER TABLE `PayrollItems` ADD `AbsenceDeduction` decimal(65,30) NOT NULL DEFAULT 0.0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260512152549_AddAbsenceTrackingToPayroll', '9.0.0');

ALTER TABLE `PayrollRuns` ADD `TotalOvertimeAmount` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `PayrollItems` ADD `OvertimeAmount` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `PayrollItems` ADD `OvertimeHours` decimal(65,30) NOT NULL DEFAULT 0.0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260512153040_AddOvertimeToPayroll', '9.0.0');

ALTER TABLE `Employees` ADD `DaysPerMonth` int NOT NULL DEFAULT 0;

ALTER TABLE `Employees` ADD `OvertimeMultiplier` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `Employees` ADD `WorkHoursPerDay` decimal(65,30) NOT NULL DEFAULT 0.0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260512153638_AddPayrollConfigToEmployee', '9.0.0');

ALTER TABLE `Departments` ADD `DaysPerMonth` int NOT NULL DEFAULT 0;

ALTER TABLE `Departments` ADD `OvertimeMultiplier` decimal(65,30) NOT NULL DEFAULT 0.0;

ALTER TABLE `Departments` ADD `WorkHoursPerDay` decimal(65,30) NOT NULL DEFAULT 0.0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260512154419_AddPayrollConfigToDepartment', '9.0.0');

ALTER TABLE `StoreSettings` ADD `BarcodeCodeFontSize` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `BarcodeMarginLeft` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `BarcodeMarginTop` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `BarcodeNameFontSize` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `BarcodePriceFontSize` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `BarcodeStoreFontSize` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `BarcodeVariantFontSize` int NOT NULL DEFAULT 0;

UPDATE `StoreSettings` SET `BarcodeCodeFontSize` = 15, `BarcodeLabelHeight` = 30, `BarcodeLabelWidth` = 50, `BarcodeMarginLeft` = 0, `BarcodeMarginTop` = 0, `BarcodeNameFontSize` = 10, `BarcodePriceFontSize` = 14, `BarcodeStoreFontSize` = 8, `BarcodeSvgHeight` = 10, `BarcodeSvgWidth` = 40, `BarcodeVariantFontSize` = 9
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260513003620_AddBarcodeAdvancedSettingsFinal', '9.0.0');

ALTER TABLE `StoreSettings` ADD `BarcodeMarginBottom` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `BarcodeMarginRight` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `BarcodePaddingBottom` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `BarcodePaddingLeft` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `BarcodePaddingRight` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `BarcodePaddingTop` int NOT NULL DEFAULT 0;

UPDATE `StoreSettings` SET `BarcodeMarginBottom` = 0, `BarcodeMarginRight` = 0, `BarcodePaddingBottom` = 0, `BarcodePaddingLeft` = 0, `BarcodePaddingRight` = 0, `BarcodePaddingTop` = 0
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260513025918_AddFullMarginsAndPaddings', '9.0.0');

ALTER TABLE `StoreSettings` ADD `BarcodeDirection` int NOT NULL DEFAULT 0;

UPDATE `StoreSettings` SET `BarcodeDirection` = 0
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260513032452_AddBarcodeDirection', '9.0.0');

ALTER TABLE `StoreSettings` ADD `ResendApiKey` varchar(200) CHARACTER SET utf8mb4 NULL;

UPDATE `StoreSettings` SET `ResendApiKey` = NULL
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260513235712_AddResendApiKey', '9.0.0');

ALTER TABLE `InventoryMovements` ADD `RemainingQty` int NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260514180509_AddRemainingQtyToInventoryMovement', '9.0.0');

ALTER TABLE `StoreSettings` MODIFY COLUMN `ReceiptDensity` double NOT NULL;

ALTER TABLE `StoreSettings` ADD `CommercialRegister` varchar(50) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `ReceiptShowCashier` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `ReceiptShowDiscount` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `ReceiptShowNote` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `ReceiptShowTax` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `ReceiptShowUnitPrice` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `TaxNumber` varchar(50) CHARACTER SET utf8mb4 NULL;

UPDATE `StoreSettings` SET `CommercialRegister` = NULL, `ReceiptDensity` = 1.3999999999999999, `ReceiptSectionsOrder` = 'header,order_info,items_table,totals_area,tafqeet,payment_info,customer_signature,footer_text,terms_conditions,barcode', `ReceiptShowCashier` = TRUE, `ReceiptShowDiscount` = TRUE, `ReceiptShowNote` = TRUE, `ReceiptShowTax` = TRUE, `ReceiptShowUnitPrice` = TRUE, `ReceiptSoftwareProvider` = 'Eng.Abdullah-Taha', `TaxNumber` = NULL
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260515094716_AddMoreReceiptSettingsV2', '9.0.0');

ALTER TABLE `PayrollItems` ADD `CommissionAmount` decimal(18,2) NOT NULL DEFAULT 0.0;

CREATE TABLE `EmployeeCommissionSettings` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmployeeId` int NOT NULL,
    `Type` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Basis` longtext CHARACTER SET utf8mb4 NOT NULL,
    `DefaultRate` decimal(18,2) NOT NULL,
    `TargetAmount` decimal(18,2) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_EmployeeCommissionSettings` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EmployeeCommissionSettings_Employees_EmployeeId` FOREIGN KEY (`EmployeeId`) REFERENCES `Employees` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `CommissionTiers` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `SettingId` int NOT NULL,
    `MinAmount` decimal(18,2) NOT NULL,
    `MaxAmount` decimal(18,2) NOT NULL,
    `Rate` decimal(18,2) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_CommissionTiers` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_CommissionTiers_EmployeeCommissionSettings_SettingId` FOREIGN KEY (`SettingId`) REFERENCES `EmployeeCommissionSettings` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_CommissionTiers_SettingId` ON `CommissionTiers` (`SettingId`);

CREATE UNIQUE INDEX `IX_EmployeeCommissionSettings_EmployeeId` ON `EmployeeCommissionSettings` (`EmployeeId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260517023542_AddEmployeeCommission', '9.0.0');

ALTER TABLE `EmployeeCommissionSettings` ADD `CommissionSchemeId` int NULL;

CREATE TABLE `CommissionSchemes` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Type` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Basis` longtext CHARACTER SET utf8mb4 NOT NULL,
    `DefaultRate` decimal(18,2) NOT NULL,
    `TargetAmount` decimal(18,2) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_CommissionSchemes` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `CommissionSchemeTiers` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `CommissionSchemeId` int NOT NULL,
    `MinAmount` decimal(18,2) NOT NULL,
    `MaxAmount` decimal(18,2) NOT NULL,
    `Rate` decimal(18,2) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_CommissionSchemeTiers` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_CommissionSchemeTiers_CommissionSchemes_CommissionSchemeId` FOREIGN KEY (`CommissionSchemeId`) REFERENCES `CommissionSchemes` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_EmployeeCommissionSettings_CommissionSchemeId` ON `EmployeeCommissionSettings` (`CommissionSchemeId`);

CREATE INDEX `IX_CommissionSchemeTiers_CommissionSchemeId` ON `CommissionSchemeTiers` (`CommissionSchemeId`);

ALTER TABLE `EmployeeCommissionSettings` ADD CONSTRAINT `FK_EmployeeCommissionSettings_CommissionSchemes_CommissionSchem~` FOREIGN KEY (`CommissionSchemeId`) REFERENCES `CommissionSchemes` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260517030204_AddCommissionSchemes', '9.0.0');

CREATE TABLE `WelcomeMessages` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Message` longtext CHARACTER SET utf8mb4 NOT NULL,
    `TargetType` longtext CHARACTER SET utf8mb4 NOT NULL,
    `TargetUserId` varchar(255) CHARACTER SET utf8mb4 NULL,
    `TargetDepartmentId` int NULL,
    `IsActive` tinyint(1) NOT NULL,
    `StartDate` datetime(6) NULL,
    `EndDate` datetime(6) NULL,
    `CreatedByUserId` varchar(255) CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_WelcomeMessages` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_WelcomeMessages_AspNetUsers_CreatedByUserId` FOREIGN KEY (`CreatedByUserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_WelcomeMessages_AspNetUsers_TargetUserId` FOREIGN KEY (`TargetUserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_WelcomeMessages_Departments_TargetDepartmentId` FOREIGN KEY (`TargetDepartmentId`) REFERENCES `Departments` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4;

CREATE TABLE `WelcomeMessageSeens` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `WelcomeMessageId` int NOT NULL,
    `UserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `SeenAt` datetime(6) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_WelcomeMessageSeens` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_WelcomeMessageSeens_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_WelcomeMessageSeens_WelcomeMessages_WelcomeMessageId` FOREIGN KEY (`WelcomeMessageId`) REFERENCES `WelcomeMessages` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_WelcomeMessages_CreatedByUserId` ON `WelcomeMessages` (`CreatedByUserId`);

CREATE INDEX `IX_WelcomeMessages_TargetDepartmentId` ON `WelcomeMessages` (`TargetDepartmentId`);

CREATE INDEX `IX_WelcomeMessages_TargetUserId` ON `WelcomeMessages` (`TargetUserId`);

CREATE INDEX `IX_WelcomeMessageSeens_UserId` ON `WelcomeMessageSeens` (`UserId`);

CREATE UNIQUE INDEX `IX_WelcomeMessageSeens_WelcomeMessageId_UserId` ON `WelcomeMessageSeens` (`WelcomeMessageId`, `UserId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260518120732_AddWelcomeMessages', '9.0.0');

ALTER TABLE `Departments` ADD `ManagerEmployeeId` int NULL;

ALTER TABLE `Departments` ADD `ParentDepartmentId` int NULL;

CREATE INDEX `IX_Departments_ManagerEmployeeId` ON `Departments` (`ManagerEmployeeId`);

CREATE INDEX `IX_Departments_ParentDepartmentId` ON `Departments` (`ParentDepartmentId`);

ALTER TABLE `Departments` ADD CONSTRAINT `FK_Departments_Departments_ParentDepartmentId` FOREIGN KEY (`ParentDepartmentId`) REFERENCES `Departments` (`Id`) ON DELETE RESTRICT;

ALTER TABLE `Departments` ADD CONSTRAINT `FK_Departments_Employees_ManagerEmployeeId` FOREIGN KEY (`ManagerEmployeeId`) REFERENCES `Employees` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260518124111_AddDepartmentHierarchyAndManager', '9.0.0');

ALTER TABLE `Employees` ADD `CommissionGroupId` int NULL;

CREATE TABLE `CommissionGroups` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `Type` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Basis` longtext CHARACTER SET utf8mb4 NOT NULL,
    `DefaultRate` decimal(18,2) NOT NULL,
    `TargetAmount` decimal(18,2) NOT NULL,
    `CommissionSchemeId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_CommissionGroups` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_CommissionGroups_CommissionSchemes_CommissionSchemeId` FOREIGN KEY (`CommissionSchemeId`) REFERENCES `CommissionSchemes` (`Id`) ON DELETE SET NULL
) CHARACTER SET=utf8mb4;

CREATE TABLE `CommissionGroupTiers` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `CommissionGroupId` int NOT NULL,
    `MinAmount` decimal(18,2) NOT NULL,
    `MaxAmount` decimal(18,2) NOT NULL,
    `Rate` decimal(18,2) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_CommissionGroupTiers` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_CommissionGroupTiers_CommissionGroups_CommissionGroupId` FOREIGN KEY (`CommissionGroupId`) REFERENCES `CommissionGroups` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_Employees_CommissionGroupId` ON `Employees` (`CommissionGroupId`);

CREATE INDEX `IX_CommissionGroups_CommissionSchemeId` ON `CommissionGroups` (`CommissionSchemeId`);

CREATE INDEX `IX_CommissionGroupTiers_CommissionGroupId` ON `CommissionGroupTiers` (`CommissionGroupId`);

ALTER TABLE `Employees` ADD CONSTRAINT `FK_Employees_CommissionGroups_CommissionGroupId` FOREIGN KEY (`CommissionGroupId`) REFERENCES `CommissionGroups` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260518164943_AddCommissionGroups', '9.0.0');

ALTER TABLE `AspNetUsers` ADD `FavoriteReports` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `AspNetUsers` ADD `PinnedSidebarItems` longtext CHARACTER SET utf8mb4 NOT NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260521120055_AddUserPreferences', '9.0.0');

ALTER TABLE `AspNetUsers` ADD `UiPreferences` longtext CHARACTER SET utf8mb4 NOT NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260521123117_AddUserUiPreferences', '9.0.0');

ALTER TABLE `InstallmentPayments` ADD `ReceiptVoucherId` int NULL;

CREATE INDEX `IX_InstallmentPayments_ReceiptVoucherId` ON `InstallmentPayments` (`ReceiptVoucherId`);

ALTER TABLE `InstallmentPayments` ADD CONSTRAINT `FK_InstallmentPayments_ReceiptVouchers_ReceiptVoucherId` FOREIGN KEY (`ReceiptVoucherId`) REFERENCES `ReceiptVouchers` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260522002613_LinkInstallmentToReceipt', '9.0.0');

CREATE TABLE `PosHeldCarts` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ReferenceId` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
    `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `Phone` varchar(20) CHARACTER SET utf8mb4 NULL,
    `ItemsJson` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Total` decimal(18,2) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_PosHeldCarts` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260522121725_AddPosHeldCart_Fixed', '9.0.0');

ALTER TABLE `Products` ADD `SizeChartImageUrl` longtext CHARACTER SET utf8mb4 NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260523113715_AddSizeChartImageUrl', '9.0.0');

CREATE TABLE `POSShiftClosures` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `StationId` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
    `ClosureDate` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `ClosedBy` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `StartingBalance` decimal(18,2) NOT NULL,
    `ExpectedCash` decimal(18,2) NOT NULL,
    `ActualCash` decimal(18,2) NOT NULL,
    `Variance` decimal(18,2) NOT NULL,
    `GrossSales` decimal(18,2) NOT NULL,
    `NetSales` decimal(18,2) NOT NULL,
    `CashSales` decimal(18,2) NOT NULL,
    `CardSales` decimal(18,2) NOT NULL,
    `VodafoneCashSales` decimal(18,2) NOT NULL,
    `InstapaySales` decimal(18,2) NOT NULL,
    `WalletSales` decimal(18,2) NOT NULL,
    `CreditSales` decimal(18,2) NOT NULL,
    `Expenses` decimal(18,2) NOT NULL,
    `SafeDrops` decimal(18,2) NOT NULL,
    `Returns` decimal(18,2) NOT NULL,
    `Discounts` decimal(18,2) NOT NULL,
    `JournalEntryReference` varchar(100) CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_POSShiftClosures` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260528003612_AddPOSShiftClosure', '9.0.0');

ALTER TABLE `Employees` ADD `AttendanceMode` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `Employees` ADD `ShiftStartTime` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `Employees` ADD `WeeklyDaysOff` longtext CHARACTER SET utf8mb4 NOT NULL;

CREATE TABLE `EmployeeAttendances` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmployeeId` int NOT NULL,
    `Date` datetime(6) NOT NULL,
    `CheckIn` datetime(6) NULL,
    `CheckOut` datetime(6) NULL,
    `WorkHours` decimal(18,2) NOT NULL,
    `OvertimeHours` decimal(18,2) NOT NULL,
    `DelayMinutes` decimal(18,2) NOT NULL,
    `IsAbsent` tinyint(1) NOT NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_EmployeeAttendances` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EmployeeAttendances_Employees_EmployeeId` FOREIGN KEY (`EmployeeId`) REFERENCES `Employees` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE UNIQUE INDEX `IX_EmployeeAttendances_EmployeeId_Date` ON `EmployeeAttendances` (`EmployeeId`, `Date`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260531101813_AddEmployeeAttendances', '9.0.0');

CREATE TABLE `ZkDevices` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `SerialNumber` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `Name` longtext CHARACTER SET utf8mb4 NOT NULL,
    `LastActive` datetime(6) NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_ZkDevices` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE UNIQUE INDEX `IX_ZkDevices_SerialNumber` ON `ZkDevices` (`SerialNumber`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260531105133_AddZkDevices', '9.0.0');

ALTER TABLE `StoreSettings` ADD `DelayGraceMinutes` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `DelayHalfDayLimitMinutes` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `DelayQuarterDayLimitMinutes` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `EnableGraduatedDelayPolicy` tinyint(1) NOT NULL DEFAULT FALSE;

UPDATE `StoreSettings` SET `DelayGraceMinutes` = 15, `DelayHalfDayLimitMinutes` = 60, `DelayQuarterDayLimitMinutes` = 30, `EnableGraduatedDelayPolicy` = TRUE
WHERE `StoreConfigId` = 1;
SELECT ROW_COUNT();


INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260531113104_AddGraduatedDelayPolicy', '9.0.0');

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260531144706_RemoveStoreSettingsSeeding', '9.0.0');

ALTER TABLE `StoreSettings` ADD `LinktreeConfig` longtext CHARACTER SET utf8mb4 NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260603104237_AddLinktreeConfig', '9.0.0');

ALTER TABLE `PayrollItems` ADD `IsPaid` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `PayrollItems` ADD `PaidAt` datetime(6) NULL;

ALTER TABLE `PayrollItems` ADD `PaymentJournalEntryId` int NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260604184610_AddPayrollItemPartialPayment', '9.0.0');

ALTER TABLE `Employees` DROP COLUMN `FixedDeduction`;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260604192055_RemoveEmployeeFixedDeduction', '9.0.0');

ALTER TABLE `StoreSettings` ADD `DailyTarget` decimal(65,30) NOT NULL DEFAULT 0.0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260605140842_AddDailyTargetToStoreSettings', '9.0.0');

ALTER TABLE `AspNetUsers` RENAME COLUMN `RefreshToken` TO `RefreshTokenHash`;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260606024146_RenameRefreshTokenToHash', '9.0.0');

CREATE TABLE `UserSessions` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `UserId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `DeviceName` longtext CHARACTER SET utf8mb4 NOT NULL,
    `DeviceFingerprint` longtext CHARACTER SET utf8mb4 NOT NULL,
    `UserAgent` longtext CHARACTER SET utf8mb4 NOT NULL,
    `IpAddress` longtext CHARACTER SET utf8mb4 NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `LastSeen` datetime(6) NOT NULL,
    `ExpiresAt` datetime(6) NULL,
    `RefreshTokenHash` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `IsRevoked` tinyint(1) NOT NULL,
    `RevokedAt` datetime(6) NULL,
    CONSTRAINT `PK_UserSessions` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_UserSessions_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE UNIQUE INDEX `IX_UserSessions_RefreshTokenHash` ON `UserSessions` (`RefreshTokenHash`);

CREATE INDEX `IX_UserSessions_UserId` ON `UserSessions` (`UserId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260606024925_AddUserSessions', '9.0.0');

ALTER TABLE `BackupRecords` ADD `Algorithm` varchar(50) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `BackupRecords` ADD `FileHash` varchar(100) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `BackupRecords` ADD `SignatureVersion` varchar(20) CHARACTER SET utf8mb4 NULL;

CREATE TABLE `SecurityEvents` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` varchar(100) CHARACTER SET utf8mb4 NULL,
    `IpAddress` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
    `Device` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `EventType` int NOT NULL,
    `Severity` int NOT NULL,
    `RiskScore` int NOT NULL,
    `CorrelationId` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_SecurityEvents` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_SecurityEvents_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_SecurityEvents_CorrelationId` ON `SecurityEvents` (`CorrelationId`);

CREATE INDEX `IX_SecurityEvents_IpAddress` ON `SecurityEvents` (`IpAddress`);

CREATE INDEX `IX_SecurityEvents_UserId` ON `SecurityEvents` (`UserId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260606034210_AddSecurityEventsAndBackupProperties', '9.0.0');

ALTER TABLE `Customers` RENAME COLUMN `Phone` TO `PhoneHash`;

ALTER TABLE `Customers` RENAME INDEX `IX_Customers_Phone` TO `IX_Customers_PhoneHash`;

ALTER TABLE `Customers` ADD `EmailEncrypted` longtext CHARACTER SET utf8mb4 NOT NULL;

ALTER TABLE `Customers` ADD `EmailHash` varchar(255) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';

ALTER TABLE `Customers` ADD `EmailKeyVersion` int NOT NULL DEFAULT 0;

ALTER TABLE `Customers` ADD `PhoneEncrypted` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `Customers` ADD `PhoneKeyVersion` int NOT NULL DEFAULT 0;

CREATE INDEX `IX_Customers_EmailHash` ON `Customers` (`EmailHash`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260606084507_AddCustomerEncryption', '9.0.0');

ALTER TABLE `AuditLogs` ADD `Hash` varchar(100) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `AuditLogs` ADD `PreviousHash` varchar(100) CHARACTER SET utf8mb4 NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260606085224_AddAuditLogChaining', '9.0.0');

ALTER TABLE `StoreSettings` ADD `ReceiptFooterFontSize` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `ReceiptHeaderFontSize` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `ReceiptItemsFontSize` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `ReceiptShowRecipientSignature` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `ReceiptShowStoreSeal` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `StoreSettings` ADD `ReceiptStoreNameFontSize` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `ReceiptTotalsFontSize` int NOT NULL DEFAULT 0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260607005740_AddReceiptSignatureAndFonts', '9.0.0');

ALTER TABLE `StoreSettings` ADD `WhatsAppInstallmentFriendlyTemplate` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `WhatsAppInstallmentNoticeTemplate` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `WhatsAppInstallmentWarningTemplate` longtext CHARACTER SET utf8mb4 NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260607013508_AddWhatsAppInstallmentTemplates', '9.0.0');

ALTER TABLE `StoreSettings` ADD `WhatsAppCancelTemplate` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `WhatsAppDeliveredTemplate` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `WhatsAppProcessingTemplate` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `WhatsAppWebsiteConfirmTemplate` longtext CHARACTER SET utf8mb4 NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260607020043_AddMoreWhatsAppTemplates', '9.0.0');

ALTER TABLE `StoreSettings` ADD `WhatsAppPaymentReminderTemplate` longtext CHARACTER SET utf8mb4 NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260607090237_AddWhatsAppPaymentReminderTemplate', '9.0.0');

ALTER TABLE `StoreSettings` ADD `WhatsAppPayrollTemplate` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `WhatsAppPosOrderTemplate` longtext CHARACTER SET utf8mb4 NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260607092203_AddPosAndPayrollWhatsAppTemplates', '9.0.0');

ALTER TABLE `PayrollItems` ADD `PaidAmount` decimal(65,30) NOT NULL DEFAULT 0.0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260607171229_AddPaidAmountToPayrollItem', '9.0.0');

CREATE TABLE `EntityAttachments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EntityType` longtext CHARACTER SET utf8mb4 NOT NULL,
    `EntityId` int NOT NULL,
    `Url` longtext CHARACTER SET utf8mb4 NOT NULL,
    `PublicId` longtext CHARACTER SET utf8mb4 NULL,
    `FileName` longtext CHARACTER SET utf8mb4 NULL,
    `ContentType` longtext CHARACTER SET utf8mb4 NULL,
    `FileSizeBytes` bigint NULL,
    `UploadedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_EntityAttachments` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260609144528_AddEntityAttachments', '9.0.0');

ALTER TABLE `ReceiptVouchers` ADD `BranchId` int NULL;

ALTER TABLE `POSShiftClosures` ADD `BranchId` int NULL;

ALTER TABLE `PaymentVouchers` ADD `BranchId` int NULL;

ALTER TABLE `Orders` ADD `BranchId` int NULL;

ALTER TABLE `Orders` ADD `WarehouseId` int NULL;

ALTER TABLE `JournalLines` ADD `BranchId` int NULL;

ALTER TABLE `InventoryMovements` ADD `WarehouseId` int NULL;

ALTER TABLE `InventoryAudits` ADD `WarehouseId` int NULL;

ALTER TABLE `Employees` ADD `BranchId` int NULL;

CREATE TABLE `Branches` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `Address` varchar(500) CHARACTER SET utf8mb4 NULL,
    `PhoneNumber` varchar(50) CHARACTER SET utf8mb4 NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_Branches` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `Warehouses` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `Location` varchar(500) CHARACTER SET utf8mb4 NULL,
    `BranchId` int NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_Warehouses` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Warehouses_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `ProductWarehouseStocks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ProductVariantId` int NOT NULL,
    `WarehouseId` int NOT NULL,
    `Quantity` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_ProductWarehouseStocks` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ProductWarehouseStocks_ProductVariants_ProductVariantId` FOREIGN KEY (`ProductVariantId`) REFERENCES `ProductVariants` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ProductWarehouseStocks_Warehouses_WarehouseId` FOREIGN KEY (`WarehouseId`) REFERENCES `Warehouses` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `StockTransfers` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `TransferNumber` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
    `SourceWarehouseId` int NOT NULL,
    `DestinationWarehouseId` int NOT NULL,
    `Status` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `ShippedAt` datetime(6) NULL,
    `ReceivedAt` datetime(6) NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `ShippedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `ReceivedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_StockTransfers` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_StockTransfers_Warehouses_DestinationWarehouseId` FOREIGN KEY (`DestinationWarehouseId`) REFERENCES `Warehouses` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_StockTransfers_Warehouses_SourceWarehouseId` FOREIGN KEY (`SourceWarehouseId`) REFERENCES `Warehouses` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE TABLE `StockTransferItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `StockTransferId` int NOT NULL,
    `ProductVariantId` int NOT NULL,
    `Quantity` int NOT NULL,
    `Note` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_StockTransferItems` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_StockTransferItems_ProductVariants_ProductVariantId` FOREIGN KEY (`ProductVariantId`) REFERENCES `ProductVariants` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_StockTransferItems_StockTransfers_StockTransferId` FOREIGN KEY (`StockTransferId`) REFERENCES `StockTransfers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_ReceiptVouchers_BranchId` ON `ReceiptVouchers` (`BranchId`);

CREATE INDEX `IX_POSShiftClosures_BranchId` ON `POSShiftClosures` (`BranchId`);

CREATE INDEX `IX_PaymentVouchers_BranchId` ON `PaymentVouchers` (`BranchId`);

CREATE INDEX `IX_Orders_BranchId` ON `Orders` (`BranchId`);

CREATE INDEX `IX_Orders_WarehouseId` ON `Orders` (`WarehouseId`);

CREATE INDEX `IX_JournalLines_BranchId` ON `JournalLines` (`BranchId`);

CREATE INDEX `IX_InventoryMovements_WarehouseId` ON `InventoryMovements` (`WarehouseId`);

CREATE INDEX `IX_InventoryAudits_WarehouseId` ON `InventoryAudits` (`WarehouseId`);

CREATE INDEX `IX_Employees_BranchId` ON `Employees` (`BranchId`);

CREATE UNIQUE INDEX `IX_ProductWarehouseStocks_ProductVariantId_WarehouseId` ON `ProductWarehouseStocks` (`ProductVariantId`, `WarehouseId`);

CREATE INDEX `IX_ProductWarehouseStocks_WarehouseId` ON `ProductWarehouseStocks` (`WarehouseId`);

CREATE INDEX `IX_StockTransferItems_ProductVariantId` ON `StockTransferItems` (`ProductVariantId`);

CREATE INDEX `IX_StockTransferItems_StockTransferId` ON `StockTransferItems` (`StockTransferId`);

CREATE INDEX `IX_StockTransfers_DestinationWarehouseId` ON `StockTransfers` (`DestinationWarehouseId`);

CREATE INDEX `IX_StockTransfers_SourceWarehouseId` ON `StockTransfers` (`SourceWarehouseId`);

CREATE UNIQUE INDEX `IX_StockTransfers_TransferNumber` ON `StockTransfers` (`TransferNumber`);

CREATE INDEX `IX_Warehouses_BranchId` ON `Warehouses` (`BranchId`);

ALTER TABLE `Employees` ADD CONSTRAINT `FK_Employees_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`) ON DELETE SET NULL;

ALTER TABLE `InventoryAudits` ADD CONSTRAINT `FK_InventoryAudits_Warehouses_WarehouseId` FOREIGN KEY (`WarehouseId`) REFERENCES `Warehouses` (`Id`) ON DELETE SET NULL;

ALTER TABLE `InventoryMovements` ADD CONSTRAINT `FK_InventoryMovements_Warehouses_WarehouseId` FOREIGN KEY (`WarehouseId`) REFERENCES `Warehouses` (`Id`) ON DELETE SET NULL;

ALTER TABLE `JournalLines` ADD CONSTRAINT `FK_JournalLines_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`) ON DELETE SET NULL;

ALTER TABLE `Orders` ADD CONSTRAINT `FK_Orders_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`) ON DELETE SET NULL;

ALTER TABLE `Orders` ADD CONSTRAINT `FK_Orders_Warehouses_WarehouseId` FOREIGN KEY (`WarehouseId`) REFERENCES `Warehouses` (`Id`) ON DELETE SET NULL;

ALTER TABLE `PaymentVouchers` ADD CONSTRAINT `FK_PaymentVouchers_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`) ON DELETE SET NULL;

ALTER TABLE `POSShiftClosures` ADD CONSTRAINT `FK_POSShiftClosures_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`) ON DELETE SET NULL;

ALTER TABLE `ReceiptVouchers` ADD CONSTRAINT `FK_ReceiptVouchers_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`) ON DELETE SET NULL;


                -- Insert default Branch if not exists
                INSERT INTO Branches (Name, Address, PhoneNumber, IsActive, CreatedAt)
                SELECT 'الفرع الرئيسي', 'المركز الرئيسي', NULL, 1, NOW(6)
                FROM (SELECT 1) AS tmp
                WHERE NOT EXISTS (SELECT 1 FROM Branches WHERE Name = 'الفرع الرئيسي');
            


                -- Insert default Warehouse if not exists
                INSERT INTO Warehouses (Name, Location, BranchId, IsActive, CreatedAt)
                SELECT 'المخزن الرئيسي', 'المركز الرئيسي', (SELECT Id FROM Branches WHERE Name = 'الفرع الرئيسي' LIMIT 1), 1, NOW(6)
                FROM (SELECT 1) AS tmp
                WHERE NOT EXISTS (SELECT 1 FROM Warehouses WHERE Name = 'المخزن الرئيسي');
            


                -- Populate ProductWarehouseStocks from current ProductVariants quantities
                INSERT INTO ProductWarehouseStocks (ProductVariantId, WarehouseId, Quantity, CreatedAt)
                SELECT pv.Id, w.Id, pv.StockQuantity, NOW(6)
                FROM ProductVariants pv
                CROSS JOIN (SELECT Id FROM Warehouses WHERE Name = 'المخزن الرئيسي' LIMIT 1) w
                WHERE NOT EXISTS (
                    SELECT 1 FROM ProductWarehouseStocks pws 
                    WHERE pws.ProductVariantId = pv.Id AND pws.WarehouseId = w.Id
                );
            


                -- Update existing Employees
                UPDATE Employees SET BranchId = (SELECT Id FROM Branches WHERE Name = 'الفرع الرئيسي' LIMIT 1) WHERE BranchId IS NULL;
            


                -- Update existing Orders
                UPDATE Orders SET 
                    BranchId = (SELECT Id FROM Branches WHERE Name = 'الفرع الرئيسي' LIMIT 1), 
                    WarehouseId = (SELECT Id FROM Warehouses WHERE Name = 'المخزن الرئيسي' LIMIT 1) 
                WHERE BranchId IS NULL;
            


                -- Update existing POS Shift Closures
                UPDATE POSShiftClosures SET BranchId = (SELECT Id FROM Branches WHERE Name = 'الفرع الرئيسي' LIMIT 1) WHERE BranchId IS NULL;
            


                -- Update existing Vouchers and Journal Lines
                UPDATE ReceiptVouchers SET BranchId = (SELECT Id FROM Branches WHERE Name = 'الفرع الرئيسي' LIMIT 1) WHERE BranchId IS NULL;
                UPDATE PaymentVouchers SET BranchId = (SELECT Id FROM Branches WHERE Name = 'الفرع الرئيسي' LIMIT 1) WHERE BranchId IS NULL;
                UPDATE JournalLines SET BranchId = (SELECT Id FROM Branches WHERE Name = 'الفرع الرئيسي' LIMIT 1) WHERE BranchId IS NULL;
            


                -- Update existing Inventory Movements and Audits
                UPDATE InventoryMovements SET WarehouseId = (SELECT Id FROM Warehouses WHERE Name = 'المخزن الرئيسي' LIMIT 1) WHERE WarehouseId IS NULL;
                UPDATE InventoryAudits SET WarehouseId = (SELECT Id FROM Warehouses WHERE Name = 'المخزن الرئيسي' LIMIT 1) WHERE WarehouseId IS NULL;
            

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260610085709_AddBranchesAndWarehouses', '9.0.0');

ALTER TABLE `PurchaseReturns` ADD `WarehouseId` int NULL;

ALTER TABLE `PurchaseInvoices` ADD `WarehouseId` int NULL;

CREATE INDEX `IX_PurchaseReturns_WarehouseId` ON `PurchaseReturns` (`WarehouseId`);

CREATE INDEX `IX_PurchaseInvoices_WarehouseId` ON `PurchaseInvoices` (`WarehouseId`);

ALTER TABLE `PurchaseInvoices` ADD CONSTRAINT `FK_PurchaseInvoices_Warehouses_WarehouseId` FOREIGN KEY (`WarehouseId`) REFERENCES `Warehouses` (`Id`) ON DELETE SET NULL;

ALTER TABLE `PurchaseReturns` ADD CONSTRAINT `FK_PurchaseReturns_Warehouses_WarehouseId` FOREIGN KEY (`WarehouseId`) REFERENCES `Warehouses` (`Id`) ON DELETE SET NULL;


                UPDATE PurchaseInvoices SET 
                    WarehouseId = (SELECT Id FROM Warehouses WHERE Name = 'المخزن الرئيسي' LIMIT 1) 
                WHERE WarehouseId IS NULL;
            


                UPDATE PurchaseReturns SET 
                    WarehouseId = (SELECT Id FROM Warehouses WHERE Name = 'المخزن الرئيسي' LIMIT 1) 
                WHERE WarehouseId IS NULL;
            

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260610094214_AddWarehouseToPurchaseInvoiceAndReturn', '9.0.0');

ALTER TABLE `AspNetUsers` ADD `BranchId` int NULL;

ALTER TABLE `AspNetUsers` ADD `WarehouseId` int NULL;

CREATE INDEX `IX_AspNetUsers_BranchId` ON `AspNetUsers` (`BranchId`);

CREATE INDEX `IX_AspNetUsers_WarehouseId` ON `AspNetUsers` (`WarehouseId`);

ALTER TABLE `AspNetUsers` ADD CONSTRAINT `FK_AspNetUsers_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`) ON DELETE SET NULL;

ALTER TABLE `AspNetUsers` ADD CONSTRAINT `FK_AspNetUsers_Warehouses_WarehouseId` FOREIGN KEY (`WarehouseId`) REFERENCES `Warehouses` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260610180726_AddBranchAndWarehouseToAppUser', '9.0.0');

ALTER TABLE `InventoryOpeningBalances` ADD `BranchId` int NULL;

ALTER TABLE `InventoryOpeningBalances` ADD `WarehouseId` int NULL;

ALTER TABLE `InventoryAudits` ADD `BranchId` int NULL;

CREATE INDEX `IX_InventoryOpeningBalances_BranchId` ON `InventoryOpeningBalances` (`BranchId`);

CREATE INDEX `IX_InventoryOpeningBalances_WarehouseId` ON `InventoryOpeningBalances` (`WarehouseId`);

CREATE INDEX `IX_InventoryAudits_BranchId` ON `InventoryAudits` (`BranchId`);

ALTER TABLE `InventoryAudits` ADD CONSTRAINT `FK_InventoryAudits_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`) ON DELETE SET NULL;

ALTER TABLE `InventoryOpeningBalances` ADD CONSTRAINT `FK_InventoryOpeningBalances_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`) ON DELETE SET NULL;

ALTER TABLE `InventoryOpeningBalances` ADD CONSTRAINT `FK_InventoryOpeningBalances_Warehouses_WarehouseId` FOREIGN KEY (`WarehouseId`) REFERENCES `Warehouses` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260610183058_AddLocationsToInventory', '9.0.0');

ALTER TABLE `StoreSettings` ADD `WebsiteWarehouseId` int NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260610201913_AddWebsiteWarehouseToSettings', '9.0.0');

ALTER TABLE `Products` ADD `SizeChartJson` longtext CHARACTER SET utf8mb4 NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260610234713_AddSizeChartJson', '9.0.0');

ALTER TABLE `StoreSettings` ADD `BusinessDayEndHour` int NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260611232700_AddBusinessDayEndHour', '9.0.0');

ALTER TABLE `Accounts` ADD `BranchId` int NULL;

CREATE INDEX `IX_Accounts_BranchId` ON `Accounts` (`BranchId`);

ALTER TABLE `Accounts` ADD CONSTRAINT `FK_Accounts_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260612112842_AddBranchIdToAccount', '9.0.0');

ALTER TABLE `PurchaseReturns` ADD `BranchId` int NULL;

ALTER TABLE `PurchaseInvoices` ADD `BranchId` int NULL;

ALTER TABLE `PayrollRuns` ADD `BranchId` int NULL;

ALTER TABLE `InventoryMovements` ADD `BranchId` int NULL;

ALTER TABLE `EmployeeDeductions` ADD `BranchId` int NULL;

ALTER TABLE `EmployeeBonuses` ADD `BranchId` int NULL;

ALTER TABLE `EmployeeAdvances` ADD `BranchId` int NULL;

CREATE INDEX `IX_PurchaseReturns_BranchId` ON `PurchaseReturns` (`BranchId`);

CREATE INDEX `IX_PurchaseInvoices_BranchId` ON `PurchaseInvoices` (`BranchId`);

CREATE INDEX `IX_PayrollRuns_BranchId` ON `PayrollRuns` (`BranchId`);

CREATE INDEX `IX_InventoryMovements_BranchId` ON `InventoryMovements` (`BranchId`);

CREATE INDEX `IX_EmployeeDeductions_BranchId` ON `EmployeeDeductions` (`BranchId`);

CREATE INDEX `IX_EmployeeBonuses_BranchId` ON `EmployeeBonuses` (`BranchId`);

CREATE INDEX `IX_EmployeeAdvances_BranchId` ON `EmployeeAdvances` (`BranchId`);

ALTER TABLE `EmployeeAdvances` ADD CONSTRAINT `FK_EmployeeAdvances_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`);

ALTER TABLE `EmployeeBonuses` ADD CONSTRAINT `FK_EmployeeBonuses_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`);

ALTER TABLE `EmployeeDeductions` ADD CONSTRAINT `FK_EmployeeDeductions_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`);

ALTER TABLE `InventoryMovements` ADD CONSTRAINT `FK_InventoryMovements_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`);

ALTER TABLE `PayrollRuns` ADD CONSTRAINT `FK_PayrollRuns_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`);

ALTER TABLE `PurchaseInvoices` ADD CONSTRAINT `FK_PurchaseInvoices_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`);

ALTER TABLE `PurchaseReturns` ADD CONSTRAINT `FK_PurchaseReturns_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260612120248_BranchIntegration', '9.0.0');

ALTER TABLE `FixedAssets` ADD `BranchId` int NULL;

ALTER TABLE `AssetDisposals` ADD `BranchId` int NULL;

ALTER TABLE `AssetDepreciations` ADD `BranchId` int NULL;

CREATE INDEX `IX_FixedAssets_BranchId` ON `FixedAssets` (`BranchId`);

CREATE INDEX `IX_AssetDisposals_BranchId` ON `AssetDisposals` (`BranchId`);

CREATE INDEX `IX_AssetDepreciations_BranchId` ON `AssetDepreciations` (`BranchId`);

ALTER TABLE `AssetDepreciations` ADD CONSTRAINT `FK_AssetDepreciations_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`);

ALTER TABLE `AssetDisposals` ADD CONSTRAINT `FK_AssetDisposals_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`);

ALTER TABLE `FixedAssets` ADD CONSTRAINT `FK_FixedAssets_Branches_BranchId` FOREIGN KEY (`BranchId`) REFERENCES `Branches` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260612131942_AddBranchIdToFixedAssets', '9.0.0');

ALTER TABLE `Branches` ADD `LinkedWarehouseId` int NULL;

ALTER TABLE `Branches` ADD `LinkedWarehouseId1` int NULL;

CREATE INDEX `IX_Branches_LinkedWarehouseId1` ON `Branches` (`LinkedWarehouseId1`);

ALTER TABLE `Branches` ADD CONSTRAINT `FK_Branches_Warehouses_LinkedWarehouseId1` FOREIGN KEY (`LinkedWarehouseId1`) REFERENCES `Warehouses` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260616211812_AddLinkedWarehouseIdToBranch', '9.0.0');

ALTER TABLE `Employees` ADD `MonthlyVacationDays` int NOT NULL DEFAULT 0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260618140649_AddMonthlyVacationDays', '9.0.0');

CREATE INDEX `IX_Orders_CustomerId` ON `Orders` (`CustomerId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260619005942_AddCompositeIndexesForPerformance', '9.0.0');

ALTER TABLE `StoreSettings` ADD `EtaClientId` varchar(200) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `EtaClientSecret` varchar(200) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `EtaEnvironment` varchar(50) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `TaxAuthorityType` int NOT NULL DEFAULT 0;

ALTER TABLE `StoreSettings` ADD `ZatcaCertificate` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `ZatcaEnvironment` varchar(50) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `Products` ADD `EgsCode` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `Orders` ADD `IsSubmittedToTaxAuthority` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `Orders` ADD `TaxAuthorityQrCode` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `Orders` ADD `TaxAuthorityReference` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `Orders` ADD `TaxAuthorityStatus` longtext CHARACTER SET utf8mb4 NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260619014114_AddTaxAuthorityFoundation', '9.0.0');

ALTER TABLE `Products` RENAME COLUMN `EgsCode` TO `SaudiProductCode`;

ALTER TABLE `StoreSettings` ADD `EtaPosSerial` varchar(100) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `EtaSignatureType` varchar(50) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `EtaTaxNumber` varchar(50) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `StoreSettings` ADD `ZatcaTaxNumber` varchar(50) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `Products` ADD `EgyptianProductCode` longtext CHARACTER SET utf8mb4 NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260619024020_AddPhase3EtaIntegration', '9.0.0');

ALTER TABLE `AspNetUsers` ADD `MaxDiscountAmount` decimal(65,30) NULL;

ALTER TABLE `AspNetUsers` ADD `MaxDiscountPercentage` decimal(65,30) NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260619133539_AddUserDiscountLimits', '9.0.0');

ALTER TABLE `AspNetUsers` ADD `PermissionsJson` longtext CHARACTER SET utf8mb4 NOT NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260620131050_AddUserPermissionsJson', '9.0.0');

CREATE TABLE `ResponsibilityTypes` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `Code` longtext CHARACTER SET utf8mb4 NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_ResponsibilityTypes` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `EmployeeTasks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmployeeId` int NOT NULL,
    `ResponsibilityTypeId` int NOT NULL,
    `Title` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Description` longtext CHARACTER SET utf8mb4 NULL,
    `TaskDate` datetime(6) NOT NULL,
    `DueDate` datetime(6) NULL,
    `Status` int NOT NULL,
    `TargetQuantity` decimal(65,30) NOT NULL,
    `CompletedQuantity` decimal(65,30) NOT NULL,
    `MaxBonusAmount` decimal(65,30) NOT NULL,
    `MaxDeductionAmount` decimal(65,30) NOT NULL,
    `ActualBonusAmount` decimal(65,30) NOT NULL,
    `ActualDeductionAmount` decimal(65,30) NOT NULL,
    `CriteriaJson` longtext CHARACTER SET utf8mb4 NULL,
    `ManagerNotes` longtext CHARACTER SET utf8mb4 NULL,
    `EmployeeNotes` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `EmployeeBonusId` int NULL,
    `EmployeeDeductionId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_EmployeeTasks` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EmployeeTasks_EmployeeBonuses_EmployeeBonusId` FOREIGN KEY (`EmployeeBonusId`) REFERENCES `EmployeeBonuses` (`Id`),
    CONSTRAINT `FK_EmployeeTasks_EmployeeDeductions_EmployeeDeductionId` FOREIGN KEY (`EmployeeDeductionId`) REFERENCES `EmployeeDeductions` (`Id`),
    CONSTRAINT `FK_EmployeeTasks_Employees_EmployeeId` FOREIGN KEY (`EmployeeId`) REFERENCES `Employees` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_EmployeeTasks_ResponsibilityTypes_ResponsibilityTypeId` FOREIGN KEY (`ResponsibilityTypeId`) REFERENCES `ResponsibilityTypes` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE TABLE `EmployeeTaskItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmployeeTaskId` int NOT NULL,
    `ProductId` int NULL,
    `ItemName` longtext CHARACTER SET utf8mb4 NULL,
    `ExpectedQuantity` decimal(65,30) NOT NULL,
    `ActualQuantity` decimal(65,30) NOT NULL,
    `IsCompleted` tinyint(1) NOT NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_EmployeeTaskItems` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EmployeeTaskItems_EmployeeTasks_EmployeeTaskId` FOREIGN KEY (`EmployeeTaskId`) REFERENCES `EmployeeTasks` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_EmployeeTaskItems_EmployeeTaskId` ON `EmployeeTaskItems` (`EmployeeTaskId`);

CREATE INDEX `IX_EmployeeTasks_EmployeeBonusId` ON `EmployeeTasks` (`EmployeeBonusId`);

CREATE INDEX `IX_EmployeeTasks_EmployeeDeductionId` ON `EmployeeTasks` (`EmployeeDeductionId`);

CREATE INDEX `IX_EmployeeTasks_EmployeeId` ON `EmployeeTasks` (`EmployeeId`);

CREATE INDEX `IX_EmployeeTasks_ResponsibilityTypeId` ON `EmployeeTasks` (`ResponsibilityTypeId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260624233653_AddEmployeeTasks', '9.0.0');

ALTER TABLE `EmployeeTasks` ADD `TaskBlueprintId` int NULL;

CREATE TABLE `TaskBlueprints` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` longtext CHARACTER SET utf8mb4 NOT NULL,
    `EmployeeId` int NOT NULL,
    `ResponsibilityTypeId` int NOT NULL,
    `StartDate` datetime(6) NOT NULL,
    `EndDate` datetime(6) NULL,
    `ActiveDaysOfWeek` longtext CHARACTER SET utf8mb4 NOT NULL,
    `TaskBehavior` longtext CHARACTER SET utf8mb4 NOT NULL,
    `TargetQuantity` decimal(65,30) NOT NULL,
    `RewardAmount` decimal(65,30) NOT NULL,
    `PenaltyAmount` decimal(65,30) NOT NULL,
    `CriteriaJson` longtext CHARACTER SET utf8mb4 NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedByUserId` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    CONSTRAINT `PK_TaskBlueprints` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_TaskBlueprints_Employees_EmployeeId` FOREIGN KEY (`EmployeeId`) REFERENCES `Employees` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_TaskBlueprints_ResponsibilityTypes_ResponsibilityTypeId` FOREIGN KEY (`ResponsibilityTypeId`) REFERENCES `ResponsibilityTypes` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_EmployeeTasks_TaskBlueprintId` ON `EmployeeTasks` (`TaskBlueprintId`);

CREATE INDEX `IX_TaskBlueprints_EmployeeId` ON `TaskBlueprints` (`EmployeeId`);

CREATE INDEX `IX_TaskBlueprints_ResponsibilityTypeId` ON `TaskBlueprints` (`ResponsibilityTypeId`);

ALTER TABLE `EmployeeTasks` ADD CONSTRAINT `FK_EmployeeTasks_TaskBlueprints_TaskBlueprintId` FOREIGN KEY (`TaskBlueprintId`) REFERENCES `TaskBlueprints` (`Id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260625013747_AddTaskBlueprints', '9.0.0');

ALTER TABLE `Employees` ADD `AllowRemotePunch` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `Branches` ADD `AllowedIpAddress` varchar(100) CHARACTER SET utf8mb4 NULL;

ALTER TABLE `Branches` ADD `AllowedPunchRadiusMeters` int NOT NULL DEFAULT 0;

ALTER TABLE `Branches` ADD `Latitude` double NULL;

ALTER TABLE `Branches` ADD `Longitude` double NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260625104848_AddPunchConstraintsToBranch', '9.0.0');

ALTER TABLE `EmployeeAttendances` ADD `PunchesJson` longtext CHARACTER SET utf8mb4 NOT NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260625110159_AddPunchesJsonToAttendance', '9.0.0');

ALTER TABLE `TaskBlueprints` ADD `TaskConfigJson` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `TaskBlueprints` ADD `TaskType` int NOT NULL DEFAULT 0;

ALTER TABLE `EmployeeTasks` ADD `ReferenceId` int NULL;

ALTER TABLE `EmployeeTasks` ADD `TaskConfigJson` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `EmployeeTasks` ADD `TaskType` int NOT NULL DEFAULT 0;

ALTER TABLE `EmployeeTaskItems` ADD `MediaUrl` longtext CHARACTER SET utf8mb4 NULL;

ALTER TABLE `EmployeeTaskItems` ADD `ProductVariantId` int NULL;

ALTER TABLE `EmployeeTaskItems` ADD `ReferenceId` int NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260625114914_AddInteractiveTasks', '9.0.0');

COMMIT;

