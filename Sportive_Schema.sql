-- ============================================================
-- Sportive API - MySQL Schema
-- شغّله في phpMyAdmin على u295059914_sportiveApi
-- ============================================================

SET FOREIGN_KEY_CHECKS = 0;

-- ASP.NET Identity Tables
CREATE TABLE IF NOT EXISTS `AspNetRoles` (
    `Id` varchar(255) NOT NULL,
    `Name` varchar(256) DEFAULT NULL,
    `NormalizedName` varchar(256) DEFAULT NULL,
    `ConcurrencyStamp` longtext DEFAULT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `RoleNameIndex` (`NormalizedName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `AspNetUsers` (
    `Id` varchar(255) NOT NULL,
    `FirstName` longtext NOT NULL,
    `LastName` longtext NOT NULL,
    `ProfileImageUrl` longtext DEFAULT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `UserName` varchar(256) DEFAULT NULL,
    `NormalizedUserName` varchar(256) DEFAULT NULL,
    `Email` varchar(256) DEFAULT NULL,
    `NormalizedEmail` varchar(256) DEFAULT NULL,
    `EmailConfirmed` tinyint(1) NOT NULL,
    `PasswordHash` longtext DEFAULT NULL,
    `SecurityStamp` longtext DEFAULT NULL,
    `ConcurrencyStamp` longtext DEFAULT NULL,
    `PhoneNumber` longtext DEFAULT NULL,
    `PhoneNumberConfirmed` tinyint(1) NOT NULL,
    `TwoFactorEnabled` tinyint(1) NOT NULL,
    `LockoutEnd` datetime(6) DEFAULT NULL,
    `LockoutEnabled` tinyint(1) NOT NULL,
    `AccessFailedCount` int NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UserNameIndex` (`NormalizedUserName`),
    KEY `EmailIndex` (`NormalizedEmail`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `AspNetRoleClaims` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RoleId` varchar(255) NOT NULL,
    `ClaimType` longtext DEFAULT NULL,
    `ClaimValue` longtext DEFAULT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_AspNetRoleClaims_RoleId` (`RoleId`),
    CONSTRAINT `FK_AspNetRoleClaims_AspNetRoles` FOREIGN KEY (`RoleId`) REFERENCES `AspNetRoles` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `AspNetUserClaims` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` varchar(255) NOT NULL,
    `ClaimType` longtext DEFAULT NULL,
    `ClaimValue` longtext DEFAULT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_AspNetUserClaims_UserId` (`UserId`),
    CONSTRAINT `FK_AspNetUserClaims_AspNetUsers` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `AspNetUserLogins` (
    `LoginProvider` varchar(255) NOT NULL,
    `ProviderKey` varchar(255) NOT NULL,
    `ProviderDisplayName` longtext DEFAULT NULL,
    `UserId` varchar(255) NOT NULL,
    PRIMARY KEY (`LoginProvider`,`ProviderKey`),
    KEY `IX_AspNetUserLogins_UserId` (`UserId`),
    CONSTRAINT `FK_AspNetUserLogins_AspNetUsers` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `AspNetUserRoles` (
    `UserId` varchar(255) NOT NULL,
    `RoleId` varchar(255) NOT NULL,
    PRIMARY KEY (`UserId`,`RoleId`),
    KEY `IX_AspNetUserRoles_RoleId` (`RoleId`),
    CONSTRAINT `FK_AspNetUserRoles_Roles` FOREIGN KEY (`RoleId`) REFERENCES `AspNetRoles` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_AspNetUserRoles_Users` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `AspNetUserTokens` (
    `UserId` varchar(255) NOT NULL,
    `LoginProvider` varchar(255) NOT NULL,
    `Name` varchar(255) NOT NULL,
    `Value` longtext DEFAULT NULL,
    PRIMARY KEY (`UserId`,`LoginProvider`,`Name`),
    CONSTRAINT `FK_AspNetUserTokens_AspNetUsers` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) NOT NULL,
    `ProductVersion` varchar(32) NOT NULL,
    PRIMARY KEY (`MigrationId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- App Tables
CREATE TABLE IF NOT EXISTS `Categories` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `NameAr` varchar(100) NOT NULL,
    `NameEn` varchar(100) NOT NULL,
    `DescriptionAr` longtext DEFAULT NULL,
    `DescriptionEn` longtext DEFAULT NULL,
    `Type` int NOT NULL,
    `ImageUrl` longtext DEFAULT NULL,
    `IsActive` tinyint(1) NOT NULL DEFAULT 1,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) DEFAULT NULL,
    `IsDeleted` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `Coupons` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Code` varchar(50) NOT NULL,
    `DescriptionAr` longtext DEFAULT NULL,
    `DescriptionEn` longtext DEFAULT NULL,
    `DiscountType` int NOT NULL,
    `DiscountValue` decimal(18,2) NOT NULL,
    `MinOrderAmount` decimal(18,2) DEFAULT NULL,
    `MaxDiscountAmount` decimal(18,2) DEFAULT NULL,
    `MaxUsageCount` int DEFAULT NULL,
    `CurrentUsageCount` int NOT NULL DEFAULT 0,
    `ExpiresAt` datetime(6) DEFAULT NULL,
    `IsActive` tinyint(1) NOT NULL DEFAULT 1,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) DEFAULT NULL,
    `IsDeleted` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `IX_Coupons_Code` (`Code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `Customers` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `FirstName` longtext NOT NULL,
    `LastName` longtext NOT NULL,
    `Email` varchar(200) NOT NULL,
    `Phone` longtext DEFAULT NULL,
    `AppUserId` varchar(255) DEFAULT NULL,
    `DateOfBirth` datetime(6) DEFAULT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) DEFAULT NULL,
    `IsDeleted` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `IX_Customers_Email` (`Email`),
    KEY `IX_Customers_AppUserId` (`AppUserId`),
    CONSTRAINT `FK_Customers_AspNetUsers` FOREIGN KEY (`AppUserId`) REFERENCES `AspNetUsers` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `Products` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `NameAr` longtext NOT NULL,
    `NameEn` longtext NOT NULL,
    `DescriptionAr` longtext DEFAULT NULL,
    `DescriptionEn` longtext DEFAULT NULL,
    `Price` decimal(18,2) NOT NULL,
    `DiscountPrice` decimal(18,2) DEFAULT NULL,
    `SKU` varchar(50) NOT NULL,
    `Brand` longtext DEFAULT NULL,
    `Status` int NOT NULL DEFAULT 0,
    `IsFeatured` tinyint(1) NOT NULL DEFAULT 0,
    `CategoryId` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) DEFAULT NULL,
    `IsDeleted` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `IX_Products_SKU` (`SKU`),
    KEY `IX_Products_CategoryId` (`CategoryId`),
    CONSTRAINT `FK_Products_Categories` FOREIGN KEY (`CategoryId`) REFERENCES `Categories` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `ProductImages` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ProductId` int NOT NULL,
    `ImageUrl` longtext NOT NULL,
    `IsMain` tinyint(1) NOT NULL DEFAULT 0,
    `SortOrder` int NOT NULL DEFAULT 0,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) DEFAULT NULL,
    `IsDeleted` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    KEY `IX_ProductImages_ProductId` (`ProductId`),
    CONSTRAINT `FK_ProductImages_Products` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `ProductVariants` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ProductId` int NOT NULL,
    `Size` longtext DEFAULT NULL,
    `Color` longtext DEFAULT NULL,
    `ColorAr` longtext DEFAULT NULL,
    `StockQuantity` int NOT NULL DEFAULT 0,
    `PriceAdjustment` decimal(18,2) DEFAULT NULL,
    `ImageUrl` longtext DEFAULT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) DEFAULT NULL,
    `IsDeleted` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    KEY `IX_ProductVariants_ProductId` (`ProductId`),
    CONSTRAINT `FK_ProductVariants_Products` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `Reviews` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ProductId` int NOT NULL,
    `CustomerId` int NOT NULL,
    `Rating` int NOT NULL,
    `Comment` longtext DEFAULT NULL,
    `IsApproved` tinyint(1) NOT NULL DEFAULT 0,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) DEFAULT NULL,
    `IsDeleted` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    KEY `IX_Reviews_ProductId` (`ProductId`),
    KEY `IX_Reviews_CustomerId` (`CustomerId`),
    CONSTRAINT `FK_Reviews_Products` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_Reviews_Customers` FOREIGN KEY (`CustomerId`) REFERENCES `Customers` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `Addresses` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `CustomerId` int NOT NULL,
    `TitleAr` longtext NOT NULL,
    `TitleEn` longtext NOT NULL,
    `Street` longtext NOT NULL,
    `City` longtext NOT NULL,
    `District` longtext DEFAULT NULL,
    `BuildingNo` longtext DEFAULT NULL,
    `Floor` longtext DEFAULT NULL,
    `ApartmentNo` longtext DEFAULT NULL,
    `AdditionalInfo` longtext DEFAULT NULL,
    `Latitude` double DEFAULT NULL,
    `Longitude` double DEFAULT NULL,
    `IsDefault` tinyint(1) NOT NULL DEFAULT 0,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) DEFAULT NULL,
    `IsDeleted` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    KEY `IX_Addresses_CustomerId` (`CustomerId`),
    CONSTRAINT `FK_Addresses_Customers` FOREIGN KEY (`CustomerId`) REFERENCES `Customers` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `Orders` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `OrderNumber` varchar(100) NOT NULL,
    `CustomerId` int NOT NULL,
    `Status` int NOT NULL DEFAULT 1,
    `FulfillmentType` int NOT NULL,
    `PaymentMethod` int NOT NULL,
    `PaymentStatus` int NOT NULL DEFAULT 1,
    `DeliveryAddressId` int DEFAULT NULL,
    `DeliveryFee` decimal(18,2) NOT NULL DEFAULT 0,
    `EstimatedDeliveryDate` datetime(6) DEFAULT NULL,
    `ActualDeliveryDate` datetime(6) DEFAULT NULL,
    `DeliveryNotes` longtext DEFAULT NULL,
    `PickupScheduledAt` datetime(6) DEFAULT NULL,
    `PickupConfirmedAt` datetime(6) DEFAULT NULL,
    `SubTotal` decimal(18,2) NOT NULL,
    `DiscountAmount` decimal(18,2) NOT NULL DEFAULT 0,
    `CouponCode` longtext DEFAULT NULL,
    `TotalAmount` decimal(18,2) NOT NULL,
    `CustomerNotes` longtext DEFAULT NULL,
    `AdminNotes` longtext DEFAULT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) DEFAULT NULL,
    `IsDeleted` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `IX_Orders_OrderNumber` (`OrderNumber`),
    KEY `IX_Orders_CustomerId` (`CustomerId`),
    KEY `IX_Orders_DeliveryAddressId` (`DeliveryAddressId`),
    CONSTRAINT `FK_Orders_Customers` FOREIGN KEY (`CustomerId`) REFERENCES `Customers` (`Id`),
    CONSTRAINT `FK_Orders_Addresses` FOREIGN KEY (`DeliveryAddressId`) REFERENCES `Addresses` (`Id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `CartItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `CustomerId` int NOT NULL,
    `ProductId` int NOT NULL,
    `ProductVariantId` int DEFAULT NULL,
    `Quantity` int NOT NULL DEFAULT 1,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) DEFAULT NULL,
    `IsDeleted` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    KEY `IX_CartItems_CustomerId` (`CustomerId`),
    KEY `IX_CartItems_ProductId` (`ProductId`),
    KEY `IX_CartItems_ProductVariantId` (`ProductVariantId`),
    CONSTRAINT `FK_CartItems_Customers` FOREIGN KEY (`CustomerId`) REFERENCES `Customers` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_CartItems_Products` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_CartItems_ProductVariants` FOREIGN KEY (`ProductVariantId`) REFERENCES `ProductVariants` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `OrderItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `OrderId` int NOT NULL,
    `ProductId` int NOT NULL,
    `ProductVariantId` int DEFAULT NULL,
    `ProductNameAr` longtext NOT NULL,
    `ProductNameEn` longtext NOT NULL,
    `Size` longtext DEFAULT NULL,
    `Color` longtext DEFAULT NULL,
    `Quantity` int NOT NULL,
    `UnitPrice` decimal(18,2) NOT NULL,
    `TotalPrice` decimal(18,2) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) DEFAULT NULL,
    `IsDeleted` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    KEY `IX_OrderItems_OrderId` (`OrderId`),
    KEY `IX_OrderItems_ProductId` (`ProductId`),
    KEY `IX_OrderItems_ProductVariantId` (`ProductVariantId`),
    CONSTRAINT `FK_OrderItems_Orders` FOREIGN KEY (`OrderId`) REFERENCES `Orders` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_OrderItems_Products` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`),
    CONSTRAINT `FK_OrderItems_ProductVariants` FOREIGN KEY (`ProductVariantId`) REFERENCES `ProductVariants` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `OrderStatusHistories` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `OrderId` int NOT NULL,
    `Status` int NOT NULL,
    `Note` longtext DEFAULT NULL,
    `ChangedByUserId` longtext DEFAULT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) DEFAULT NULL,
    `IsDeleted` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    KEY `IX_OrderStatusHistories_OrderId` (`OrderId`),
    CONSTRAINT `FK_OrderStatusHistories_Orders` FOREIGN KEY (`OrderId`) REFERENCES `Orders` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Mark migration as applied
INSERT IGNORE INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260321_InitialMySQL', '9.0.0');

-- Seed Categories
INSERT IGNORE INTO `Categories` (`Id`, `NameAr`, `NameEn`, `Type`, `IsActive`, `CreatedAt`, `IsDeleted`)
VALUES
(1, 'رجالي',         'Men',              1, 1, '2024-01-01 00:00:00', 0),
(2, 'حريمي',         'Women',            2, 1, '2024-01-01 00:00:00', 0),
(3, 'أطفال',         'Kids',             3, 1, '2024-01-01 00:00:00', 0),
(4, 'أدوات رياضية', 'Sports Equipment', 4, 1, '2024-01-01 00:00:00', 0);

SET FOREIGN_KEY_CHECKS = 1;

SELECT 'Schema created successfully!' AS Result;
