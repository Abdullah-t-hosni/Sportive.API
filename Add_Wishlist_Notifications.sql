-- ============================================================
-- Sportive — إضافة جداول Wishlist و Notifications
-- شغّله في phpMyAdmin بعد السكيما الأساسية
-- ============================================================

CREATE TABLE IF NOT EXISTS `WishlistItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `CustomerId` int NOT NULL,
    `ProductId` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) DEFAULT NULL,
    `IsDeleted` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    KEY `IX_WishlistItems_CustomerId` (`CustomerId`),
    KEY `IX_WishlistItems_ProductId` (`ProductId`),
    CONSTRAINT `FK_WishlistItems_Customers` FOREIGN KEY (`CustomerId`) REFERENCES `Customers` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_WishlistItems_Products` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `Notifications` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `CustomerId` int NOT NULL,
    `TitleAr` longtext NOT NULL,
    `TitleEn` longtext NOT NULL,
    `MessageAr` longtext NOT NULL,
    `MessageEn` longtext NOT NULL,
    `Type` varchar(50) NOT NULL DEFAULT 'info',
    `IsRead` tinyint(1) NOT NULL DEFAULT 0,
    `ActionUrl` longtext DEFAULT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) DEFAULT NULL,
    `IsDeleted` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    KEY `IX_Notifications_CustomerId` (`CustomerId`),
    KEY `IX_Notifications_IsRead` (`IsRead`),
    CONSTRAINT `FK_Notifications_Customers` FOREIGN KEY (`CustomerId`) REFERENCES `Customers` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

SELECT 'WishlistItems and Notifications tables created!' AS Result;
