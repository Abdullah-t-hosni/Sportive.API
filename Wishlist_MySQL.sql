-- أضف الجدول ده في phpMyAdmin
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

SELECT 'WishlistItems table created!' AS Result;
