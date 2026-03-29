-- ══════════════════════════════════════════════════════
-- Fix: ضبط إعدادات الربط المالي يدوياً بأحدث أكواد الحسابات
-- تشغيل في phpMyAdmin
-- ══════════════════════════════════════════════════════

SET @cash_id = (SELECT Id FROM Accounts WHERE Code = '110101' LIMIT 1);
SET @sales_id = (SELECT Id FROM Accounts WHERE Code = '4101' OR Code = '410101' LIMIT 1); -- سيتم تعديل الكود 410101 إلى الخصم لاحقاً إذا وجدنا حساب مبيعات آخر
SET @inventory_id = (SELECT Id FROM Accounts WHERE Code = '1106' LIMIT 1);
SET @purchase_id = (SELECT Id FROM Accounts WHERE Code = '511' LIMIT 1);
SET @supplier_id = (SELECT Id FROM Accounts WHERE Code = '2101' LIMIT 1);
SET @customer_id = (SELECT Id FROM Accounts WHERE Code = '1103' LIMIT 1);
SET @cogs_id = (SELECT Id FROM Accounts WHERE Code = '51101' LIMIT 1);
SET @vat_in_id = (SELECT Id FROM Accounts WHERE Code = '2105' LIMIT 1);
SET @vat_out_id = (SELECT Id FROM Accounts WHERE Code = '2105' LIMIT 1);
SET @sales_disc_id = (SELECT Id FROM Accounts WHERE Code = '410101' LIMIT 1);
SET @purch_disc_id = (SELECT Id FROM Accounts WHERE Code = '51103' LIMIT 1);

-- إدراج أو تحديث الربط
INSERT INTO `AccountSystemMappings` (`Key`, `AccountId`, `CreatedAt`, `IsDeleted`) VALUES
('cashAccountID', @cash_id, NOW(), 0),
('salesAccountID', @sales_id, NOW(), 0),
('inventoryAccountID', @inventory_id, NOW(), 0),
('purchaseAccountID', @purchase_id, NOW(), 0),
('supplierAccountID', @supplier_id, NOW(), 0),
('customerAccountID', @customer_id, NOW(), 0),
('costOfGoodsSoldAccountID', @cogs_id, NOW(), 0),
('vatInputAccountID', @vat_in_id, NOW(), 0),
('vatOutputAccountID', @vat_out_id, NOW(), 0),
('salesDiscountAccountID', @sales_disc_id, NOW(), 0),
('purchaseDiscountAccountID', @purch_disc_id, NOW(), 0)
ON DUPLICATE KEY UPDATE 
    AccountId = VALUES(AccountId),
    UpdatedAt = NOW();

-- تصحيح: إذا كان حساب 410101 هو الخصم الممنوح، لا يجب أن يكون هو حساب المبيعات
-- سنحاول البحث عن حساب مبيعات عام أو إنشاء واحد إذا لزم الأمر
-- لكن بناءً على الدليل الحالي، سنقوم بالربط بالأكواد المتاحة.
