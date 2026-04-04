-- ══════════════════════════════════════════════════════
-- Seed: شجرة الحسابات الكاملة (متزامن مع Accounts.xlsx)
-- توليد تلقائي: python tools/sync_coa_from_xlsx.py
-- يُصلح تلقائياً: تكرار 110103 الثاني → 110104 (العجز والزيادة)
-- قم بتشغيل السكربت في قاعدة البيانات بعد أخذ نسخة احتياطية
-- ══════════════════════════════════════════════════════

SET FOREIGN_KEY_CHECKS = 0;
DELETE FROM `JournalLines`;
DELETE FROM `ReceiptVouchers`;
DELETE FROM `PaymentVouchers`;
DELETE FROM `JournalEntries`;
DELETE FROM `Accounts`;

ALTER TABLE `JournalLines` AUTO_INCREMENT = 1;
ALTER TABLE `ReceiptVouchers` AUTO_INCREMENT = 1;
ALTER TABLE `PaymentVouchers` AUTO_INCREMENT = 1;
ALTER TABLE `JournalEntries` AUTO_INCREMENT = 1;
ALTER TABLE `Accounts` AUTO_INCREMENT = 1;
SET FOREIGN_KEY_CHECKS = 1;

-- Type: 1=Asset, 2=Liability, 3=Equity, 4=Revenue, 5=Expense
-- Nature: 1=Debit, 2=Credit — الربط بالأب يُشتق من أطول بادئة رقمية للكود

-- 1. الأصول
INSERT INTO `Accounts` (`Code`,`NameAr`,`Description`,`Type`,`Nature`,`ParentId`,`Level`,`IsLeaf`,`AllowPosting`,`IsSystem`,`CreatedAt`) VALUES
('1', 'الأصول', NULL, 1, 1, NULL, 1, 0, 0, 1, NOW()),
('11', 'أصول متداولة', NULL, 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1') x), 2, 0, 0, 1, NOW()),
('12', 'أصول ثابتة', NULL, 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1') x), 2, 0, 0, 1, NOW()),
('1101', 'النقدية والصناديق', 'النقدية وما في حكمها (في الخزينة والعهد)', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='11') x), 3, 0, 0, 1, NOW()),
('1102', 'النقدية في البنك', 'النقدية في البنوك', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='11') x), 3, 0, 0, 1, NOW()),
('1103', 'العملاء', 'مبالغ مستحقة على حساب العملاء (بالأجل)', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='11') x), 3, 1, 0, 1, NOW()),
('1104', 'مصروفات مقدمة', 'مصروف مدفوع مقدماً مثل التأمين وسلف الموظفين وإيجار المكتب', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='11') x), 3, 0, 0, 1, NOW()),
('1105', 'سلف الموظفين', 'سلف الموظفين يلتزم الموظف بسدادها حسب المتفق عليه', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='11') x), 3, 1, 0, 1, NOW()),
('1106', 'المخزون', 'المخزون ويشمل المواد أولية وتامة الصنع', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='11') x), 3, 1, 0, 1, NOW()),
('1107', 'النقدية في المحافظ', 'النقدية في المحافظ', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='11') x), 3, 0, 0, 1, NOW()),
('1201', 'عقارات وآلات ومعدات وأثاث', 'الممتلكات والآلات والمعدات', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='12') x), 3, 0, 0, 1, NOW()),
('1202', 'الأصول غير الملموسة', 'الأصول غير الملموسة مثل حق الشهرة وبراءة الاختراع وحقوق النسخ والعلامات التجارية', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='12') x), 3, 1, 0, 1, NOW()),
('110101', 'نقدية الكاشير', 'النقدية في الخزينة', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1101') x), 4, 1, 1, 1, NOW()),
('110102', 'نقدية الموقع ( كاش عند الاستلام )', 'النقدية في الخزينة', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1101') x), 4, 1, 0, 1, NOW()),
('110103', 'نقدية الحسابات', 'النقدية في الخزينة', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1101') x), 4, 1, 1, 1, NOW()),
('110104', 'العجز والزيادة', 'النقدية في الخزينة', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1101') x), 4, 1, 1, 1, NOW()),
('110201', 'حساب البنك', 'حساب البنك الجاري - اسم البنك', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1102') x), 4, 1, 1, 1, NOW()),
('110402', 'إيجار مقدم', 'إيجار مدفوع مقدماً يتم إطفاء مايخص السنة المالية إلى مصروف', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1104') x), 4, 1, 0, 1, NOW()),
('110405', 'مصاريف البرامج المقدمة', NULL, 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1104') x), 4, 1, 0, 1, NOW()),
('110701', 'محفظة فودافون كاش', 'محفظة فودافون كاش', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1107') x), 4, 1, 1, 1, NOW()),
('110702', 'محفظة فودافون كاش  ( الموقع )', 'محفظة فودافون كاش', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1107') x), 4, 1, 1, 1, NOW()),
('110703', 'انستاباي', 'انستاباي', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1107') x), 4, 1, 1, 1, NOW()),
('110704', 'انستاباي ( الموقع )', 'انستاباي', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1107') x), 4, 1, 1, 1, NOW()),
('120101', 'الأراضي', 'الأراضي الممتلكة من قبل المنشأة', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1201') x), 4, 1, 0, 1, NOW()),
('120102', 'المباني والتأسيس', 'المباني التي تستخدم في عمليات الشركة مثل المخازن والمكاتب والمصانع والمستودعات', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1201') x), 4, 1, 0, 1, NOW()),
('120103', 'المعدات', 'المعدات المستخدمة في عمليات التشغيل', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1201') x), 4, 1, 0, 1, NOW()),
('120104', 'أجهزة مكتبية وطابعات وجوالات', 'أجهزة مكتبية مثل الحاسب الآلي ، الجهاز المحمول وطابعات', 1, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='1201') x), 4, 1, 0, 1, NOW());

-- 2. الالتزامات
INSERT INTO `Accounts` (`Code`,`NameAr`,`Description`,`Type`,`Nature`,`ParentId`,`Level`,`IsLeaf`,`AllowPosting`,`IsSystem`,`CreatedAt`) VALUES
('2', 'الالتزامات', NULL, 2, 2, NULL, 1, 0, 0, 1, NOW()),
('21', 'الالتزامات المتداولة', NULL, 2, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='2') x), 2, 0, 0, 1, NOW()),
('22', 'التزامات غير متداولة', NULL, 2, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='2') x), 2, 0, 0, 1, NOW()),
('2101', 'الموردين', 'مبالغ مستحقة لحسابات الموردين (بالأجل)', 2, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='21') x), 3, 1, 0, 1, NOW()),
('2102', 'مصروفات مستحقة', 'مصروفات مستحقة على المنشأة لم يتم سدادها أو تسجيلها بعد', 2, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='21') x), 3, 0, 0, 1, NOW()),
('2103', 'الرواتب المستحقة', 'رواتب مستحقة على المنشأة لم يتم سدادها بعد', 2, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='21') x), 3, 1, 0, 1, NOW()),
('2104', 'قروض قصيرة الأجل', 'قروض متوقع سداده خلال عام أو فترة مالية أيهما أطول', 2, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='21') x), 3, 1, 0, 1, NOW()),
('2105', 'ضريبة القيمة المضافة المستحقة', 'ضريبة القيمة المضافة مستحقة الدفع لهيئة الزكاة والدخل', 2, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='21') x), 3, 1, 0, 1, NOW()),
('2106', 'الضرائب المستحقة', 'ضريبة الدخل المستحقة عن الشركات الأجنبية', 2, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='21') x), 3, 1, 0, 1, NOW()),
('2107', 'إيرادات غير مكتسبة', 'مبالغ حصلت عليها المنشأة قبل تسليم البضاعة أو تقديم الخدمة', 2, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='21') x), 3, 1, 0, 1, NOW()),
('2109', 'مجمع الاستهلاك', 'مجمع استهلاك الأصول', 2, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='21') x), 3, 0, 0, 1, NOW()),
('2201', 'قروض طويلة أجل', 'قروض طويلة الأجل مستحق سدادها خلال أكثر من عام أو فترة مالية أيهما أطول', 2, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='22') x), 3, 1, 0, 1, NOW()),
('210202', 'مصروفات مستحقة عمولات البائعين', NULL, 2, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='2102') x), 4, 1, 0, 1, NOW()),
('210901', 'مجمع استهلاك المباني', 'مجمع استهلاك المباني', 2, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='2109') x), 4, 1, 0, 1, NOW()),
('210902', 'مجمع استهلاك المعدات', 'مجمع استهلاك المعدات', 2, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='2109') x), 4, 1, 0, 1, NOW()),
('210903', 'مجمع استهلاك أجهزة مكتبية وطابعات', 'مجمع استهلاك أجهزة مكتبية وطابعات', 2, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='2109') x), 4, 1, 0, 1, NOW());

-- 3. حقوق الملكية
INSERT INTO `Accounts` (`Code`,`NameAr`,`Description`,`Type`,`Nature`,`ParentId`,`Level`,`IsLeaf`,`AllowPosting`,`IsSystem`,`CreatedAt`) VALUES
('3', 'حقوق الملكية', NULL, 3, 2, NULL, 1, 0, 0, 1, NOW()),
('31', 'رأس المال', NULL, 3, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='3') x), 2, 0, 0, 1, NOW()),
('32', 'حقوق ملكية أخرى', NULL, 3, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='3') x), 2, 0, 0, 1, NOW()),
('33', 'احتياطيات', NULL, 3, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='3') x), 2, 0, 0, 1, NOW()),
('34', 'الأرباح المبقاة (أو الخسائر)', NULL, 3, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='3') x), 2, 0, 0, 1, NOW()),
('3101', 'رأس المال المسجل', 'رأس المال المسجل في السجل التجاري', 3, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='31') x), 3, 1, 0, 1, NOW()),
('3102', 'رأس المال الإضافي المدفوع', 'رأس المال إضافي مدفوع من قبل المستثمرين لزيادة حقوق الملكية', 3, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='31') x), 3, 1, 0, 1, NOW()),
('3201', 'أرصدة افتتاحية', 'الأرصدة الافتتاحية', 3, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='32') x), 3, 1, 0, 1, NOW()),
('3205', 'جاري الشركاء', 'جاري الشركاء', 3, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='32') x), 3, 0, 0, 1, NOW()),
('320501', 'جاري الدكتور محمد', 'جاري الشريك محمد', 3, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='3205') x), 4, 1, 1, 1, NOW()),
('320502', 'جاري ابراهيم', 'جاري الشريك ابراهيم', 3, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='3205') x), 4, 1, 1, 1, NOW()),
('320503', 'جاري حتاته', 'جاري الشريك حتاته', 3, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='3205') x), 4, 1, 1, 1, NOW()),
('3301', 'احتياطي نظامي', 'تجنيب 10% من صافي الربح حتى يصل إلى 30% من رأس المال حسب نظام الشركات', 3, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='33') x), 3, 1, 0, 1, NOW()),
('3401', 'الأرباح والخسائر العاملة', 'صافي الربح أو الخسارة للفترة المالية الحالية', 3, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='34') x), 3, 1, 0, 1, NOW()),
('3402', 'الأرباح المبقاة (أو الخسائر)', 'أرباح مبقاة لغرض إعادة استثمارها في أعمال المنشأة', 3, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='34') x), 3, 1, 0, 1, NOW());

-- 4. الإيرادات
INSERT INTO `Accounts` (`Code`,`NameAr`,`Description`,`Type`,`Nature`,`ParentId`,`Level`,`IsLeaf`,`AllowPosting`,`IsSystem`,`CreatedAt`) VALUES
('4', 'الإيرادات', NULL, 4, 2, NULL, 1, 0, 0, 1, NOW()),
('41', 'الإيرادات التشغيلية', NULL, 4, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='4') x), 2, 0, 0, 1, NOW()),
('42', 'الإيرادات غير التشغيلية', NULL, 4, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='4') x), 2, 0, 0, 1, NOW()),
('4101', 'إيرادات المبيعات/ الخدمات', 'الدخل الناتج من بيع سلعة أو تقديم خدمة', 4, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='41') x), 3, 0, 0, 1, NOW()),
('4102', 'مرتجع المبيعات', NULL, 4, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='41') x), 3, 1, 0, 1, NOW()),
('4201', 'إيرادات أخرى', 'إيراد نتج من أنشطة أخرى للمنشأة غير النشاط الأساسي', 4, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='42') x), 3, 1, 0, 1, NOW()),
('410101', 'الخصم الممنوح', NULL, 4, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='4101') x), 4, 1, 0, 1, NOW()),
('4103', 'حساب التوصيل', 'إيرادات خدمات التوصيل والشحن', 4, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='41') x), 3, 0, 0, 1, NOW()),
('410301', 'إيرادات التوصيل', 'إيرادات محصلة من العملاء مقابل التوصيل', 4, 2, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='4103') x), 4, 1, 1, 1, NOW());

-- 5. المصاريف
INSERT INTO `Accounts` (`Code`,`NameAr`,`Description`,`Type`,`Nature`,`ParentId`,`Level`,`IsLeaf`,`AllowPosting`,`IsSystem`,`CreatedAt`) VALUES
('5', 'المصاريف', NULL, 5, 1, NULL, 1, 0, 0, 1, NOW()),
('51', 'التكاليف المباشرة', NULL, 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='5') x), 2, 0, 0, 1, NOW()),
('52', 'التكاليف التشغيلية', NULL, 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='5') x), 2, 0, 0, 1, NOW()),
('53', 'مصاريف غير التشغيلية', NULL, 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='5') x), 2, 0, 0, 1, NOW()),
('511', 'صافي المشتريات', NULL, 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='51') x), 3, 0, 0, 1, NOW()),
('512', 'مصاريف الرواتب', NULL, 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='51') x), 3, 0, 0, 1, NOW()),
('521', 'المصاريف الإدارية', NULL, 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='52') x), 3, 0, 0, 1, NOW()),
('522', 'المصاريف التشغيلية', NULL, 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='52') x), 3, 0, 0, 1, NOW()),
('523', 'مصاريف الإهلاك', 'إهلاك الأصول الثابتة', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='52') x), 3, 0, 0, 1, NOW()),
('5301', 'الزكاة', 'زكاة تدفع لهيئة الزكاة والدخل', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='53') x), 3, 1, 0, 1, NOW()),
('51101', 'تكلفة البضاعة المباعة', 'تكلفة البضاعة المباعة', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='511') x), 4, 1, 0, 1, NOW()),
('51102', 'شحن وتخليص جمركي', 'شحن وتخليص جمركي للبضاعة المستوردة من الخارج', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='511') x), 4, 1, 0, 1, NOW()),
('51103', 'خصم مكتسب ( المشتريات)', NULL, 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='511') x), 4, 1, 0, 1, NOW()),
('51201', 'رواتب وأجور', 'رواتب وأجور الموظفين العاملين في النشاط الأساسي للمنشأة', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='512') x), 4, 1, 0, 1, NOW()),
('51202', 'عمولات البيع', 'عمولات البيع', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='512') x), 4, 1, 0, 1, NOW()),
('52101', 'الرواتب والرسوم الإدارية', 'رواتب وأجور الموظفين الإداريين', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='521') x), 4, 1, 0, 1, NOW()),
('52104', 'التأمينات الاجتماعية', 'نسبة التأمينات الاجتماعية تدفع شهرياً', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='521') x), 4, 1, 0, 1, NOW()),
('52105', 'الرسوم الحكومية', 'مثل رسوم تجديد السجل التجاري والبلدية وختم الغرفة التجارية', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='521') x), 4, 0, 0, 1, NOW()),
('52106', 'مصاريف خدمات المكتب ( الكهرباء والنت )', 'فواتير الماء والكهرباء والهاتف والانترنت', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='521') x), 4, 1, 0, 1, NOW()),
('52201', 'مصاريف تسويقية ودعائية', 'مصاريف تسويقية ودعائية', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='522') x), 4, 1, 0, 1, NOW()),
('52202', 'مصاريف الإيجار', 'إيجار المكتب', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='522') x), 4, 1, 0, 1, NOW()),
('52203', 'رسوم واشتراكات', 'رسوم اشتراكات', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='522') x), 4, 1, 0, 1, NOW()),
('52204', 'مصاريف مكتبية ومطبوعات', 'قرطاسية وطباعة', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='522') x), 4, 1, 0, 1, NOW()),
('52205', 'مصاريف ضيافة', 'ضيافة ونظافة تخص المنشأة', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='522') x), 4, 1, 0, 1, NOW()),
('52206', 'عمولات بنكية', 'رسوم بنكية عند تحويل من بنك محلي إلى بنك محلي آخر أو لطباعة كشف حساب مختوم', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='522') x), 4, 1, 0, 1, NOW()),
('52207', 'مصاريف أخرى', 'مصاريف أخرى متنوعة', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='522') x), 4, 0, 0, 1, NOW()),
('52208', 'مصروف نقل ومواصلات ومحروقات', 'مصروف نقل ومواصلات (بنزين ، أجرة)', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='522') x), 4, 1, 0, 1, NOW()),
('52301', 'مصروف إهلاك المباني والأثاث', 'مصروف إهلاك المباني', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='523') x), 4, 1, 0, 1, NOW()),
('52302', 'مصروف إهلاك المعدات', 'مصروف إهلاك المعدات', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='523') x), 4, 1, 0, 1, NOW()),
('52303', 'مصروف إهلاك أجهزة مكتبية وطابعات والجوالات', 'مصروف إهلاك أجهزة مكتبية وطابعات', 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='523') x), 4, 1, 0, 1, NOW()),
('5210501', 'رسوم حكومية أخرى', NULL, 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='52105') x), 5, 1, 0, 1, NOW()),
('5210502', 'مصروف غرامات حكومية متنوعة', NULL, 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='52105') x), 5, 1, 0, 1, NOW()),
('5220701', 'مصروفات صيانة', NULL, 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='52207') x), 5, 1, 0, 1, NOW()),
('5220702', 'مصاريف طبية', NULL, 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='52207') x), 5, 1, 0, 1, NOW()),
('5220703', 'مكافأة تشجيعية', NULL, 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='52207') x), 5, 1, 0, 1, NOW()),
('5220704', 'مصروفات نثرية', NULL, 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='52207') x), 5, 1, 0, 1, NOW()),
('5220705', 'أدوات استهلاكية', NULL, 5, 1, (SELECT Id FROM (SELECT Id FROM Accounts WHERE Code='52207') x), 5, 1, 0, 1, NOW());
