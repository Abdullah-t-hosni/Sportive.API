-- ============================================================
-- Sportive — New Features Migration
-- شغّله في phpMyAdmin
-- ============================================================

-- ─── 1. WishlistItems ────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `WishlistItems` (
    `Id`         int NOT NULL AUTO_INCREMENT,
    `CustomerId` int NOT NULL,
    `ProductId`  int NOT NULL,
    `CreatedAt`  datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `UpdatedAt`  datetime(6) DEFAULT NULL,
    `IsDeleted`  tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `IX_WishlistItems_Customer_Product` (`CustomerId`, `ProductId`),
    KEY `IX_WishlistItems_CustomerId` (`CustomerId`),
    KEY `IX_WishlistItems_ProductId` (`ProductId`),
    CONSTRAINT `FK_WishlistItems_Customers` FOREIGN KEY (`CustomerId`) REFERENCES `Customers` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_WishlistItems_Products`  FOREIGN KEY (`ProductId`)  REFERENCES `Products`  (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ─── 2. Notifications ────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `Notifications` (
    `Id`        int NOT NULL AUTO_INCREMENT,
    `UserId`    varchar(255) NOT NULL,
    `TitleAr`   longtext NOT NULL,
    `TitleEn`   longtext NOT NULL,
    `MessageAr` longtext NOT NULL,
    `MessageEn` longtext NOT NULL,
    `Type`      int NOT NULL DEFAULT 6,
    `IsRead`    tinyint(1) NOT NULL DEFAULT 0,
    `OrderId`   int DEFAULT NULL,
    `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `UpdatedAt` datetime(6) DEFAULT NULL,
    `IsDeleted` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`Id`),
    KEY `IX_Notifications_UserId` (`UserId`),
    KEY `IX_Notifications_UserId_IsRead` (`UserId`, `IsRead`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ─── تحقق ────────────────────────────────────────────────────
SELECT 'WishlistItems' AS `Table`,
    (SELECT COUNT(*) FROM information_schema.tables 
     WHERE table_schema = DATABASE() AND table_name = 'WishlistItems') AS `Exists`
UNION ALL
SELECT 'Notifications',
    (SELECT COUNT(*) FROM information_schema.tables 
     WHERE table_schema = DATABASE() AND table_name = 'Notifications');
