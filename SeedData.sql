-- ============================================================
-- Sportive — Seed Data للتجربة
-- شغّله في SQL Server Management Studio على SportiveDB
-- ============================================================

USE SportiveDB;
GO

-- ─── 1. Products ─────────────────────────────────────────────
INSERT INTO Products (NameAr, NameEn, DescriptionAr, DescriptionEn, Price, DiscountPrice, SKU, Brand, [Status], IsFeatured, CategoryId, CreatedAt, IsDeleted)
VALUES
-- رجالي
(N'تيشيرت نايك رياضي', N'Nike Sport T-Shirt', N'تيشيرت رياضي خفيف الوزن مصنوع من قماش DriFit لامتصاص العرق', N'Lightweight DriFit sport t-shirt for superior moisture wicking', 299, 199, 'NK-TSH-001', 'Nike', 0, 1, 1, GETUTCDATE(), 0),
(N'شورت أديداس تدريب', N'Adidas Training Shorts', N'شورت مريح للتدريب مع جيوب جانبية وخصر مطاط', N'Comfortable training shorts with side pockets and elastic waist', 249, NULL, 'AD-SHT-001', 'Adidas', 0, 1, 1, GETUTCDATE(), 0),
(N'تراكسوت بوما كامل', N'Puma Full Tracksuit', N'بدلة رياضية كاملة مناسبة للركض والتدريب', N'Full tracksuit suitable for running and training', 799, 649, 'PM-TRK-001', 'Puma', 0, 1, 1, GETUTCDATE(), 0),
(N'حذاء نايك رن', N'Nike Run Shoe', N'حذاء ركض احترافي بنعل هوائي لأفضل أداء', N'Professional running shoe with air sole for best performance', 1299, 999, 'NK-SHO-001', 'Nike', 0, 1, 1, GETUTCDATE(), 0),

-- حريمي
(N'تيشيرت يوغا نسائي', N'Women Yoga T-Shirt', N'تيشيرت ليونة عالية مناسب لليوغا والبيلاتس', N'High flexibility shirt suitable for yoga and pilates', 349, 249, 'YG-TSH-F01', 'Under Armour', 0, 1, 2, GETUTCDATE(), 0),
(N'ليجنز رياضية نسائية', N'Women Sport Leggings', N'ليجنز مريحة بخامة مضادة للرطوبة', N'Comfortable moisture-resistant leggings', 449, 349, 'UA-LGS-F01', 'Under Armour', 0, 1, 2, GETUTCDATE(), 0),
(N'حذاء أديداس نسائي', N'Adidas Women Sneaker', N'حذاء رياضي أنيق للمرأة مناسب للجيم والخروج', N'Elegant women sport shoe for gym and casual wear', 899, NULL, 'AD-SHO-F01', 'Adidas', 0, 1, 2, GETUTCDATE(), 0),

-- أطفال
(N'بدلة أطفال رياضية', N'Kids Sport Set', N'بدلة رياضية للأطفال بألوان مبهجة وخامة ناعمة', N'Kids sport set with bright colors and soft fabric', 299, 199, 'KD-SET-001', 'Nike', 0, 1, 3, GETUTCDATE(), 0),
(N'حذاء أطفال ملون', N'Kids Colorful Sneaker', N'حذاء ملون للأطفال بتصميم مرح وخف', N'Colorful kids sneaker with fun and lightweight design', 399, NULL, 'KD-SHO-001', 'Adidas', 0, 1, 3, GETUTCDATE(), 0),

-- أدوات رياضية
(N'كرة قدم احترافية', N'Professional Football', N'كرة قدم مطابقة للمواصفات الدولية FIFA', N'FIFA standard professional football', 349, 299, 'EQ-FTB-001', 'Adidas', 0, 1, 4, GETUTCDATE(), 0),
(N'حبل تخطي احترافي', N'Pro Jump Rope', N'حبل تخطي بمقابض مريحة وسرعة عالية', N'Jump rope with comfortable handles and high speed', 149, 99, 'EQ-JRP-001', 'Under Armour', 0, 1, 4, GETUTCDATE(), 0),
(N'دمبلز 5 كيلو زوج', N'5KG Dumbbells Pair', N'دمبلز لبناء العضلات مصنوعة من حديد مطلي', N'Muscle building dumbbells made of coated iron', 599, NULL, 'EQ-DML-001', 'Generic', 0, 0, 4, GETUTCDATE(), 0);
GO

-- ─── 2. Product Variants ─────────────────────────────────────
-- تيشيرت نايك (Id=1)
INSERT INTO ProductVariants (ProductId, Size, Color, ColorAr, StockQuantity, PriceAdjustment, CreatedAt, IsDeleted)
VALUES
(1, 'S',   'Black', N'أسود', 15, 0, GETUTCDATE(), 0),
(1, 'M',   'Black', N'أسود', 20, 0, GETUTCDATE(), 0),
(1, 'L',   'Black', N'أسود', 18, 0, GETUTCDATE(), 0),
(1, 'XL',  'Black', N'أسود', 10, 0, GETUTCDATE(), 0),
(1, 'S',   'White', N'أبيض', 12, 0, GETUTCDATE(), 0),
(1, 'M',   'White', N'أبيض', 15, 0, GETUTCDATE(), 0),
(1, 'L',   'White', N'أبيض', 8,  0, GETUTCDATE(), 0),
(1, 'S',   'Navy',  N'كحلي', 10, 0, GETUTCDATE(), 0),
(1, 'M',   'Navy',  N'كحلي', 12, 0, GETUTCDATE(), 0);

-- شورت أديداس (Id=2)
INSERT INTO ProductVariants (ProductId, Size, Color, ColorAr, StockQuantity, PriceAdjustment, CreatedAt, IsDeleted)
VALUES
(2, 'S',  'Black',  N'أسود',  20, 0, GETUTCDATE(), 0),
(2, 'M',  'Black',  N'أسود',  25, 0, GETUTCDATE(), 0),
(2, 'L',  'Black',  N'أسود',  15, 0, GETUTCDATE(), 0),
(2, 'XL', 'Black',  N'أسود',  10, 0, GETUTCDATE(), 0),
(2, 'M',  'Navy',   N'كحلي',  18, 0, GETUTCDATE(), 0),
(2, 'L',  'Navy',   N'كحلي',  12, 0, GETUTCDATE(), 0);

-- حذاء نايك (Id=4)
INSERT INTO ProductVariants (ProductId, Size, Color, ColorAr, StockQuantity, PriceAdjustment, CreatedAt, IsDeleted)
VALUES
(4, '40', 'Black/White', N'أسود/أبيض', 8,  0,   GETUTCDATE(), 0),
(4, '41', 'Black/White', N'أسود/أبيض', 10, 0,   GETUTCDATE(), 0),
(4, '42', 'Black/White', N'أسود/أبيض', 12, 0,   GETUTCDATE(), 0),
(4, '43', 'Black/White', N'أسود/أبيض', 9,  0,   GETUTCDATE(), 0),
(4, '44', 'Black/White', N'أسود/أبيض', 5,  100, GETUTCDATE(), 0),
(4, '40', 'Red/Black',   N'أحمر/أسود', 7,  0,   GETUTCDATE(), 0),
(4, '42', 'Red/Black',   N'أحمر/أسود', 8,  0,   GETUTCDATE(), 0);

-- تيشيرت يوغا (Id=5)
INSERT INTO ProductVariants (ProductId, Size, Color, ColorAr, StockQuantity, PriceAdjustment, CreatedAt, IsDeleted)
VALUES
(5, 'XS', 'Pink',   N'وردي',  12, 0, GETUTCDATE(), 0),
(5, 'S',  'Pink',   N'وردي',  15, 0, GETUTCDATE(), 0),
(5, 'M',  'Pink',   N'وردي',  18, 0, GETUTCDATE(), 0),
(5, 'S',  'Purple', N'بنفسجي',10, 0, GETUTCDATE(), 0),
(5, 'M',  'Purple', N'بنفسجي',14, 0, GETUTCDATE(), 0);

-- ليجنز (Id=6)
INSERT INTO ProductVariants (ProductId, Size, Color, ColorAr, StockQuantity, PriceAdjustment, CreatedAt, IsDeleted)
VALUES
(6, 'XS', 'Black',  N'أسود',  10, 0, GETUTCDATE(), 0),
(6, 'S',  'Black',  N'أسود',  15, 0, GETUTCDATE(), 0),
(6, 'M',  'Black',  N'أسود',  20, 0, GETUTCDATE(), 0),
(6, 'L',  'Black',  N'أسود',  12, 0, GETUTCDATE(), 0),
(6, 'S',  'Gray',   N'رمادي', 8,  0, GETUTCDATE(), 0),
(6, 'M',  'Gray',   N'رمادي', 10, 0, GETUTCDATE(), 0);
GO

-- ─── 3. Customers ────────────────────────────────────────────
INSERT INTO Customers (FirstName, LastName, Email, Phone, CreatedAt, IsDeleted)
VALUES
(N'أحمد',   N'محمد',    'ahmed@test.com',   '01012345678', GETUTCDATE(), 0),
(N'سارة',   N'علي',     'sara@test.com',    '01123456789', GETUTCDATE(), 0),
(N'محمد',   N'إبراهيم', 'mohamed@test.com', '01234567890', GETUTCDATE(), 0),
(N'فاطمة',  N'حسن',     'fatma@test.com',   '01098765432', GETUTCDATE(), 0),
(N'عمر',    N'خالد',    'omar@test.com',    '01587654321', GETUTCDATE(), 0);
GO

-- ─── 4. Addresses ────────────────────────────────────────────
INSERT INTO Addresses (CustomerId, TitleAr, TitleEn, Street, City, District, BuildingNo, Floor, ApartmentNo, IsDefault, CreatedAt, IsDeleted)
VALUES
(1, N'المنزل', 'Home', N'شارع التحرير',   N'القاهرة', N'وسط البلد', '15', '3', 'A', 1, GETUTCDATE(), 0),
(2, N'المنزل', 'Home', N'شارع الهرم',     N'الجيزة',  N'الهرم',     '8',  '1', '2', 1, GETUTCDATE(), 0),
(3, N'المنزل', 'Home', N'شارع الكورنيش',  N'الإسكندرية', N'سيدي بشر', '22', '4', 'B', 1, GETUTCDATE(), 0);
GO

-- ─── 5. Orders ───────────────────────────────────────────────
INSERT INTO Orders (OrderNumber, CustomerId, [Status], FulfillmentType, PaymentMethod, PaymentStatus, DeliveryAddressId, DeliveryFee, SubTotal, DiscountAmount, TotalAmount, CreatedAt, UpdatedAt, IsDeleted)
VALUES
('SP-20260101-0001', 1, 6, 1, 1, 2, 1, 50, 498,  0,   548,  DATEADD(day, -15, GETUTCDATE()), NULL, 0),
('SP-20260110-0002', 2, 6, 1, 3, 2, 2, 50, 249,  0,   299,  DATEADD(day, -10, GETUTCDATE()), NULL, 0),
('SP-20260115-0003', 3, 5, 1, 1, 1, 3, 50, 1299, 0,   1349, DATEADD(day, -5,  GETUTCDATE()), NULL, 0),
('SP-20260118-0004', 1, 3, 2, 1, 1, NULL, 0, 799, 150, 649, DATEADD(day, -3,  GETUTCDATE()), NULL, 0),
('SP-20260120-0005', 4, 2, 1, 4, 2, NULL, 50, 449, 0,  499,  DATEADD(day, -2,  GETUTCDATE()), NULL, 0),
('SP-20260121-0006', 5, 1, 1, 1, 1, NULL, 50, 348, 0,  398,  DATEADD(day, -1,  GETUTCDATE()), NULL, 0),
('SP-20260321-0007', 2, 1, 2, 1, 1, NULL, 0,  199, 0,  199,  GETUTCDATE(),                   NULL, 0);
GO

-- ─── 6. Order Items ──────────────────────────────────────────
INSERT INTO OrderItems (OrderId, ProductId, ProductNameAr, ProductNameEn, Size, Color, Quantity, UnitPrice, TotalPrice, CreatedAt, IsDeleted)
VALUES
-- Order 1
(1, 1, N'تيشيرت نايك رياضي',  'Nike Sport T-Shirt',  'M', 'Black', 2, 199, 398, GETUTCDATE(), 0),
(1, 11, N'حبل تخطي احترافي',   'Pro Jump Rope',       NULL, NULL,   1, 99,  99,  GETUTCDATE(), 0),
-- Order 2
(2, 2, N'شورت أديداس تدريب',   'Adidas Training Shorts','L','Black',1, 249, 249, GETUTCDATE(), 0),
-- Order 3
(3, 4, N'حذاء نايك رن',        'Nike Run Shoe',       '42','Black/White',1,999,999,GETUTCDATE(),0),
(3, 11,N'حبل تخطي احترافي',    'Pro Jump Rope',       NULL,NULL,   3, 99,  297, GETUTCDATE(), 0),
-- Order 4
(4, 3, N'تراكسوت بوما كامل',   'Puma Full Tracksuit', 'L', 'Black',1, 649, 649, GETUTCDATE(), 0),
-- Order 5
(5, 6, N'ليجنز رياضية نسائية', 'Women Sport Leggings','M', 'Black',1, 349, 349, GETUTCDATE(), 0),
-- Order 6
(6, 8, N'بدلة أطفال رياضية',   'Kids Sport Set',      'M', 'Blue', 1, 199, 199, GETUTCDATE(), 0),
(6, 9, N'حذاء أطفال ملون',     'Kids Colorful Sneaker','36',NULL,  1, 399, 399, GETUTCDATE(), 0),
-- Order 7
(7, 1, N'تيشيرت نايك رياضي',  'Nike Sport T-Shirt',  'S', 'White',1, 199, 199, GETUTCDATE(), 0);
GO

-- ─── 7. Order Status History ─────────────────────────────────
INSERT INTO OrderStatusHistories (OrderId, [Status], Note, CreatedAt, IsDeleted)
VALUES
(1, 1, N'تم استلام الطلب',      DATEADD(day,-15,GETUTCDATE()), 0),
(1, 2, N'تم تأكيد الطلب',      DATEADD(day,-15,GETUTCDATE()), 0),
(1, 3, N'جاري تحضير الطلب',    DATEADD(day,-14,GETUTCDATE()), 0),
(1, 5, N'خرج مع المندوب',      DATEADD(day,-14,GETUTCDATE()), 0),
(1, 6, N'تم التسليم بنجاح',    DATEADD(day,-13,GETUTCDATE()), 0),
(2, 1, N'تم استلام الطلب',      DATEADD(day,-10,GETUTCDATE()), 0),
(2, 6, N'تم التسليم',           DATEADD(day,-9, GETUTCDATE()), 0),
(3, 1, N'تم استلام الطلب',      DATEADD(day,-5, GETUTCDATE()), 0),
(3, 2, N'تم التأكيد',           DATEADD(day,-5, GETUTCDATE()), 0),
(3, 5, N'خرج للتوصيل',         DATEADD(day,-4, GETUTCDATE()), 0),
(4, 1, N'تم استلام الطلب',      DATEADD(day,-3, GETUTCDATE()), 0),
(4, 3, N'قيد التحضير',          DATEADD(day,-2, GETUTCDATE()), 0),
(5, 1, N'Order placed',         DATEADD(day,-2, GETUTCDATE()), 0),
(5, 2, N'Confirmed',            DATEADD(day,-1, GETUTCDATE()), 0),
(6, 1, N'Order placed',         DATEADD(day,-1, GETUTCDATE()), 0),
(7, 1, N'Order placed',         GETUTCDATE(),                  0);
GO

-- ─── 8. Coupons ──────────────────────────────────────────────
INSERT INTO Coupons (Code, DescriptionAr, DescriptionEn, DiscountType, DiscountValue, MinOrderAmount, MaxDiscountAmount, MaxUsageCount, CurrentUsageCount, ExpiresAt, IsActive, CreatedAt, IsDeleted)
VALUES
('SPORT10', N'خصم 10% على كل المشتريات', '10% off all purchases',     0, 10,  200,  100, 100, 0, DATEADD(month,3,GETUTCDATE()), 1, GETUTCDATE(), 0),
('WELCOME', N'خصم 50 جنيه للعملاء الجدد', '50 EGP off for new customers', 1, 50,  300,  NULL, 500, 0, DATEADD(month,6,GETUTCDATE()), 1, GETUTCDATE(), 0),
('SUMMER25', N'خصم 25% لحملة الصيف',       'Summer 25% off',           0, 25,  500,  200, 50,  0, DATEADD(month,1,GETUTCDATE()), 1, GETUTCDATE(), 0);
GO

-- ─── تحقق من البيانات ────────────────────────────────────────
SELECT 'Products'  AS [Table], COUNT(*) AS [Count] FROM Products  WHERE IsDeleted=0
UNION ALL
SELECT 'Variants',  COUNT(*) FROM ProductVariants WHERE IsDeleted=0
UNION ALL
SELECT 'Customers', COUNT(*) FROM Customers       WHERE IsDeleted=0
UNION ALL
SELECT 'Orders',    COUNT(*) FROM Orders           WHERE IsDeleted=0
UNION ALL
SELECT 'OrderItems',COUNT(*) FROM OrderItems       WHERE IsDeleted=0
UNION ALL
SELECT 'Coupons',   COUNT(*) FROM Coupons          WHERE IsDeleted=0;
GO

PRINT N'✅ Seed data inserted successfully!';
