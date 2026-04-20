namespace Sportive.API.Models;

// ══════════════════════════════════════════════════════
// الأصول الثابتة — Fixed Asset Models
// ══════════════════════════════════════════════════════

// ─── Enums ────────────────────────────────────────────

public enum DepreciationMethod
{
    StraightLine      = 1,  // القسط الثابت
    DecliningBalance  = 2,  // القسط المتناقص
    UnitsOfProduction = 3,  // وحدات الإنتاج
}

public enum AssetStatus
{
    Active           = 1,  // نشط
    FullyDepreciated = 2,  // مستهلك بالكامل
    Disposed         = 3,  // مستبعد
    UnderMaintenance = 4,  // تحت الصيانة
    Idle             = 5,  // متوقف
}

public enum DisposalType
{
    Sale     = 1,  // بيع
    Scrap    = 2,  // خردة
    Donation = 3,  // تبرع / هبة
    Loss     = 4,  // فقد / تلف
    Transfer = 5,  // تحويل
}

// ══════════════════════════════════════════════════════
// تبويبة الأصول — Fixed Asset Category
// ══════════════════════════════════════════════════════

public class FixedAssetCategory : BaseEntity
{
    public string  Name        { get; set; } = string.Empty; // اسم الفئة (أثاث، معدات، سيارات…)
    public string? Description { get; set; }
    public bool    IsActive    { get; set; } = true;

    // ربط محاسبي افتراضي للفئة (يمكن تجاوزه على مستوى الأصل)
    public int?    AssetAccountId              { get; set; } // حساب الأصل (مدين)
    public Account? AssetAccount              { get; set; }
    public int?    AccumDepreciationAccountId  { get; set; } // حساب مجمع الإهلاك (دائن)
    public Account? AccumDepreciationAccount  { get; set; }
    public int?    DepreciationExpenseAccountId { get; set; } // حساب مصروف الإهلاك (مدين)
    public Account? DepreciationExpenseAccount { get; set; }

    public OrderSource? CostCenter { get; set; } // مركز التكلفة الافتراضي (موقع أو POS)

    public ICollection<FixedAsset> Assets { get; set; } = new List<FixedAsset>();
}

// ══════════════════════════════════════════════════════
// تبويبة الأصول — Fixed Asset
// ══════════════════════════════════════════════════════

public class FixedAsset : BaseEntity
{
    public string  AssetNumber   { get; set; } = string.Empty; // رقم الأصل (FA-0001)
    public string  Name          { get; set; } = string.Empty; // اسم الأصل
    public string? Description   { get; set; }

    public int             CategoryId { get; set; }
    public FixedAssetCategory Category { get; set; } = null!;

    // بيانات الشراء
    public DateTime PurchaseDate  { get; set; }
    public decimal  PurchaseCost  { get; set; } = 0;   // تكلفة الشراء
    public string?  Supplier      { get; set; }         // اسم المورد (نصي اختياري)
    public int?     PurchaseInvoiceId { get; set; }     // ربط بفاتورة مشتريات
    public PurchaseInvoice? PurchaseInvoice { get; set; }

    // بيانات الإهلاك
    public DepreciationMethod DepreciationMethod { get; set; } = DepreciationMethod.StraightLine;
    public int     UsefulLifeYears  { get; set; } = 1;  // العمر الإنتاجي بالسنوات
    public decimal SalvageValue     { get; set; } = 0;  // القيمة التخريدية
    public DateTime? DepreciationStartDate { get; set; } // تاريخ بدء الإهلاك

    // أرصدة محسوبة (تُحدَّث بعد كل قيد)
    public decimal AccumulatedDepreciation { get; set; } = 0; // مجمع الإهلاك
    public decimal BookValue => PurchaseCost - AccumulatedDepreciation;  // القيمة الدفترية

    public AssetStatus Status { get; set; } = AssetStatus.Active;

    // ملاحظات ومرفقات
    public string? Location        { get; set; }  // موقع الأصل
    public string? SerialNumber    { get; set; }  // الرقم التسلسلي
    public string? Notes           { get; set; }
    public string? AttachmentUrl   { get; set; }
    public string? AttachmentPublicId { get; set; }
    public string? CreatedByUserId { get; set; }

    // ربط محاسبي على مستوى الأصل (يُلغي افتراضي الفئة إن وُجد)
    public int?    AssetAccountId              { get; set; }
    public Account? AssetAccount              { get; set; }
    public int?    AccumDepreciationAccountId  { get; set; }
    public Account? AccumDepreciationAccount  { get; set; }
    public int?    DepreciationExpenseAccountId { get; set; }
    public Account? DepreciationExpenseAccount { get; set; }

    public OrderSource? CostCenter { get; set; } // مركز التكلفة للأصل (يسمع في القيود)

    public ICollection<AssetDepreciation> Depreciations { get; set; } = new List<AssetDepreciation>();
    public ICollection<AssetDisposal>     Disposals     { get; set; } = new List<AssetDisposal>();
}

// ══════════════════════════════════════════════════════
// تبويبة الإهلاك — Asset Depreciation
// ══════════════════════════════════════════════════════

public class AssetDepreciation : BaseEntity
{
    public string  DepreciationNumber { get; set; } = string.Empty; // رقم قيد الإهلاك (DEP-0001)

    public int        FixedAssetId { get; set; }
    public FixedAsset FixedAsset   { get; set; } = null!;

    public DateTime DepreciationDate   { get; set; }           // تاريخ الإهلاك
    public int      PeriodYear         { get; set; }           // السنة
    public int      PeriodMonth        { get; set; }           // الشهر (1-12)

    public decimal  DepreciationAmount     { get; set; } = 0;  // قسط الإهلاك
    public decimal  AccumulatedBefore      { get; set; } = 0;  // مجمع الإهلاك قبل القيد
    public decimal  AccumulatedAfter       { get; set; } = 0;  // مجمع الإهلاك بعد القيد
    public decimal  BookValueAfter         { get; set; } = 0;  // القيمة الدفترية بعد القيد

    public string?  Notes            { get; set; }
    public string?  CreatedByUserId  { get; set; }

    // ربط بالقيد المحاسبي
    public int?          JournalEntryId { get; set; }
    public JournalEntry? JournalEntry   { get; set; }
}

// ══════════════════════════════════════════════════════
// تبويبة الاستبعادات — Asset Disposal
// ══════════════════════════════════════════════════════

public class AssetDisposal : BaseEntity
{
    public string  DisposalNumber { get; set; } = string.Empty; // رقم مستند الاستبعاد (DIS-0001)

    public int        FixedAssetId { get; set; }
    public FixedAsset FixedAsset   { get; set; } = null!;

    public DisposalType DisposalType  { get; set; } = DisposalType.Sale;
    public DateTime     DisposalDate  { get; set; }

    // قيم وقت الاستبعاد
    public decimal  BookValueAtDisposal       { get; set; } = 0;  // القيمة الدفترية لحظة الاستبعاد
    public decimal  AccumulatedAtDisposal     { get; set; } = 0;  // مجمع الإهلاك لحظة الاستبعاد
    public decimal  SaleProceeds              { get; set; } = 0;  // عائد البيع (إن وجد)

    // ربح / خسارة الاستبعاد (SaleProceeds - BookValueAtDisposal)
    public decimal  GainLossOnDisposal => SaleProceeds - BookValueAtDisposal;

    // حسابات الاستبعاد
    public int?    ProceedsAccountId { get; set; }   // حساب المتحصلات (الخزينة أو العميل)
    public Account? ProceedsAccount  { get; set; }
    public int?    GainAccountId     { get; set; }   // حساب أرباح الاستبعاد
    public Account? GainAccount      { get; set; }
    public int?    LossAccountId     { get; set; }   // حساب خسائر الاستبعاد
    public Account? LossAccount      { get; set; }

    public string?  Buyer            { get; set; }   // اسم المشتري (إن كان بيع)
    public string?  Notes            { get; set; }
    public string?  AttachmentUrl    { get; set; }
    public string?  AttachmentPublicId { get; set; }
    public string?  CreatedByUserId  { get; set; }

    // ربط بالقيد المحاسبي
    public int?          JournalEntryId { get; set; }
    public JournalEntry? JournalEntry   { get; set; }
}
