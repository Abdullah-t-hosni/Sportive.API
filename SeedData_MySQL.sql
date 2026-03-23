-- ============================================================
-- Sportive — MySQL Seed Data
-- شغّله في phpMyAdmin على u282618987_sportiveApi
-- ============================================================

SET FOREIGN_KEY_CHECKS = 0;

-- ─── 1. Products ─────────────────────────────────────────────
INSERT IGNORE INTO `Products`
  (`NameAr`,`NameEn`,`DescriptionAr`,`DescriptionEn`,`Price`,`DiscountPrice`,`SKU`,`Brand`,`Status`,`IsFeatured`,`CategoryId`,`CreatedAt`,`IsDeleted`)
VALUES
('تيشيرت نايك رياضي','Nike Sport T-Shirt','تيشيرت رياضي خفيف الوزن DriFit','Lightweight DriFit sport t-shirt',299,199,'NK-TSH-001','Nike',0,1,1,NOW(),0),
('شورت أديداس تدريب','Adidas Training Shorts','شورت مريح للتدريب مع جيوب جانبية','Comfortable training shorts with side pockets',249,NULL,'AD-SHT-001','Adidas',0,1,1,NOW(),0),
('تراكسوت بوما كامل','Puma Full Tracksuit','بدلة رياضية كاملة للركض والتدريب','Full tracksuit suitable for running and training',799,649,'PM-TRK-001','Puma',0,1,1,NOW(),0),
('حذاء نايك رن','Nike Run Shoe','حذاء ركض احترافي بنعل هوائي','Professional running shoe with air sole',1299,999,'NK-SHO-001','Nike',0,1,1,NOW(),0),
('تيشيرت يوغا نسائي','Women Yoga T-Shirt','تيشيرت مرن مناسب لليوغا والبيلاتس','High flexibility shirt for yoga and pilates',349,249,'YG-TSH-F01','Under Armour',0,1,2,NOW(),0),
('ليجنز رياضية نسائية','Women Sport Leggings','ليجنز مريحة بخامة مضادة للرطوبة','Comfortable moisture-resistant leggings',449,349,'UA-LGS-F01','Under Armour',0,1,2,NOW(),0),
('حذاء أديداس نسائي','Adidas Women Sneaker','حذاء رياضي أنيق للمرأة','Elegant women sport shoe for gym',899,NULL,'AD-SHO-F01','Adidas',0,1,2,NOW(),0),
('بدلة أطفال رياضية','Kids Sport Set','بدلة رياضية للأطفال بألوان مبهجة','Kids sport set with bright colors',299,199,'KD-SET-001','Nike',0,1,3,NOW(),0),
('حذاء أطفال ملون','Kids Colorful Sneaker','حذاء ملون للأطفال بتصميم مرح','Colorful kids sneaker with fun design',399,NULL,'KD-SHO-001','Adidas',0,1,3,NOW(),0),
('كرة قدم احترافية','Professional Football','كرة قدم FIFA standard','FIFA standard professional football',349,299,'EQ-FTB-001','Adidas',0,1,4,NOW(),0),
('حبل تخطي احترافي','Pro Jump Rope','حبل تخطي بمقابض مريحة','Jump rope with comfortable handles',149,99,'EQ-JRP-001','Under Armour',0,1,4,NOW(),0),
('دمبلز 5 كيلو زوج','5KG Dumbbells Pair','دمبلز لبناء العضلات من حديد مطلي','Muscle building coated iron dumbbells',599,NULL,'EQ-DML-001','Generic',0,0,4,NOW(),0);

-- ─── 2. Product Variants ─────────────────────────────────────
-- نايك تيشيرت (Id=1)
INSERT IGNORE INTO `ProductVariants` (`ProductId`,`Size`,`Color`,`ColorAr`,`StockQuantity`,`PriceAdjustment`,`CreatedAt`,`IsDeleted`) VALUES
(1,'S','Black','أسود',15,0,NOW(),0),(1,'M','Black','أسود',20,0,NOW(),0),
(1,'L','Black','أسود',18,0,NOW(),0),(1,'XL','Black','أسود',10,0,NOW(),0),
(1,'S','White','أبيض',12,0,NOW(),0),(1,'M','White','أبيض',15,0,NOW(),0),
(1,'S','Navy','كحلي',10,0,NOW(),0),(1,'M','Navy','كحلي',12,0,NOW(),0);

-- أديداس شورت (Id=2)
INSERT IGNORE INTO `ProductVariants` (`ProductId`,`Size`,`Color`,`ColorAr`,`StockQuantity`,`PriceAdjustment`,`CreatedAt`,`IsDeleted`) VALUES
(2,'S','Black','أسود',20,0,NOW(),0),(2,'M','Black','أسود',25,0,NOW(),0),
(2,'L','Black','أسود',15,0,NOW(),0),(2,'XL','Black','أسود',10,0,NOW(),0),
(2,'M','Navy','كحلي',18,0,NOW(),0),(2,'L','Navy','كحلي',12,0,NOW(),0);

-- نايك حذاء (Id=4)
INSERT IGNORE INTO `ProductVariants` (`ProductId`,`Size`,`Color`,`ColorAr`,`StockQuantity`,`PriceAdjustment`,`CreatedAt`,`IsDeleted`) VALUES
(4,'40','Black/White','أسود/أبيض',8,0,NOW(),0),(4,'41','Black/White','أسود/أبيض',10,0,NOW(),0),
(4,'42','Black/White','أسود/أبيض',12,0,NOW(),0),(4,'43','Black/White','أسود/أبيض',9,0,NOW(),0),
(4,'44','Black/White','أسود/أبيض',5,100,NOW(),0),
(4,'42','Red/Black','أحمر/أسود',8,0,NOW(),0),(4,'43','Red/Black','أحمر/أسود',6,0,NOW(),0);

-- يوغا تيشيرت (Id=5)
INSERT IGNORE INTO `ProductVariants` (`ProductId`,`Size`,`Color`,`ColorAr`,`StockQuantity`,`PriceAdjustment`,`CreatedAt`,`IsDeleted`) VALUES
(5,'XS','Pink','وردي',12,0,NOW(),0),(5,'S','Pink','وردي',15,0,NOW(),0),
(5,'M','Pink','وردي',18,0,NOW(),0),(5,'S','Purple','بنفسجي',10,0,NOW(),0),
(5,'M','Purple','بنفسجي',14,0,NOW(),0);

-- ليجنز (Id=6)
INSERT IGNORE INTO `ProductVariants` (`ProductId`,`Size`,`Color`,`ColorAr`,`StockQuantity`,`PriceAdjustment`,`CreatedAt`,`IsDeleted`) VALUES
(6,'XS','Black','أسود',10,0,NOW(),0),(6,'S','Black','أسود',15,0,NOW(),0),
(6,'M','Black','أسود',20,0,NOW(),0),(6,'L','Black','أسود',12,0,NOW(),0),
(6,'S','Gray','رمادي',8,0,NOW(),0),(6,'M','Gray','رمادي',10,0,NOW(),0);

-- ─── 3. Coupons ──────────────────────────────────────────────
INSERT IGNORE INTO `Coupons`
  (`Code`,`DescriptionAr`,`DescriptionEn`,`DiscountType`,`DiscountValue`,`MinOrderAmount`,`MaxDiscountAmount`,`MaxUsageCount`,`CurrentUsageCount`,`ExpiresAt`,`IsActive`,`CreatedAt`,`IsDeleted`)
VALUES
('SPORT10','خصم 10% على كل المشتريات','10% off all purchases',0,10,200,100,100,0,DATE_ADD(NOW(), INTERVAL 3 MONTH),1,NOW(),0),
('WELCOME','خصم 50 جنيه للعملاء الجدد','50 EGP off for new customers',1,50,300,NULL,500,0,DATE_ADD(NOW(), INTERVAL 6 MONTH),1,NOW(),0),
('SUMMER25','خصم 25% لحملة الصيف','Summer 25% off',0,25,500,200,50,0,DATE_ADD(NOW(), INTERVAL 1 MONTH),1,NOW(),0);

-- ─── 4. Customers تجريبيين ───────────────────────────────────
INSERT IGNORE INTO `Customers` (`FirstName`,`LastName`,`Email`,`Phone`,`CreatedAt`,`IsDeleted`) VALUES
('أحمد','محمد','ahmed@test.com','01012345678',NOW(),0),
('سارة','علي','sara@test.com','01123456789',NOW(),0),
('محمد','إبراهيم','mohamed@test.com','01234567890',NOW(),0);

SET FOREIGN_KEY_CHECKS = 1;

-- ─── تحقق ────────────────────────────────────────────────────
SELECT 'Products'  AS `Table`, COUNT(*) AS `Count` FROM `Products`  WHERE `IsDeleted`=0
UNION ALL SELECT 'Variants',   COUNT(*) FROM `ProductVariants` WHERE `IsDeleted`=0
UNION ALL SELECT 'Coupons',    COUNT(*) FROM `Coupons`         WHERE `IsDeleted`=0
UNION ALL SELECT 'Customers',  COUNT(*) FROM `Customers`       WHERE `IsDeleted`=0;
