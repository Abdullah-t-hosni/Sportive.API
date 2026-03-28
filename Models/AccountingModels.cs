namespace Sportive.API.Models;

// ══════════════════════════════════════════════════════
// شجرة الحسابات
// ══════════════════════════════════════════════════════

public enum AccountType
{
    Asset      = 1,  // أصول
    Liability  = 2,  // التزامات
    Equity     = 3,  // حقوق ملكية
    Revenue    = 4,  // إيرادات
    Expense    = 5,  // مصاريف
}

public enum AccountNature
{
    Debit  = 1,  // مدين
    Credit = 2,  // دائن
}

public class Account : BaseEntity
{
    public string   Code        { get; set; } = string.Empty;  // رمز الحساب (مثل 110101)
    public string   NameAr      { get; set; } = string.Empty;  // اسم الحساب عربي
    public string?  NameEn      { get; set; }                  // اسم الحساب إنجليزي
    public string?  Description { get; set; }
    public AccountType   Type   { get; set; }
    public AccountNature Nature { get; set; }                  // طبيعة الحساب
    public int?     ParentId    { get; set; }                  // الحساب الأب
    public Account? Parent      { get; set; }
    public int      Level       { get; set; } = 1;             // مستوى الحساب (1-6)
    public bool     IsLeaf      { get; set; } = true;          // يمكن الترحيل إليه
    public bool     AllowPosting{ get; set; } = false;         // يقبل قيود مباشرة
    public bool     IsActive    { get; set; } = true;
    public bool     IsSystem    { get; set; } = false;         // لا يمكن حذفه
    public decimal  OpeningBalance { get; set; } = 0;          // الرصيد الافتتاحي
    public ICollection<Account>      Children { get; set; } = new List<Account>();
    public ICollection<JournalLine>  Lines    { get; set; } = new List<JournalLine>();
}

// ══════════════════════════════════════════════════════
// القيود المحاسبية (Journal Entries)
// ══════════════════════════════════════════════════════

public enum JournalEntryType
{
    Manual          = 1,   // قيد يدوي
    SalesInvoice    = 2,   // فاتورة مبيعات
    SalesReturn     = 3,   // مرتجع مبيعات
    PurchaseInvoice = 4,   // فاتورة مشتريات
    PurchaseReturn  = 5,   // مرتجع مشتريات
    ReceiptVoucher  = 6,   // سند قبض
    PaymentVoucher  = 7,   // سند دفع
    OpeningBalance  = 8,   // قيد الأرصدة الافتتاحية
}

public enum JournalEntryStatus
{
    Draft    = 1,  // مسودة
    Posted   = 2,  // مرحّل
    Reversed = 3,  // معكوس
}

public class JournalEntry : BaseEntity
{
    public string             EntryNumber { get; set; } = string.Empty;  // رقم القيد
    public DateTime           EntryDate   { get; set; } = DateTime.UtcNow;
    public JournalEntryType   Type        { get; set; } = JournalEntryType.Manual;
    public JournalEntryStatus Status      { get; set; } = JournalEntryStatus.Draft;
    public string?            Reference   { get; set; }  // مرجع (رقم فاتورة، سند، ...)
    public string?            Description { get; set; }  // البيان
    public string?            CreatedByUserId { get; set; }
    public int?               ReversalOfId    { get; set; }   // إذا كان قيد عكسي
    public JournalEntry?      ReversalOf      { get; set; }
    public string?            AttachmentUrl   { get; set; }
    public string?            AttachmentPublicId { get; set; }

    public ICollection<JournalLine> Lines { get; set; } = new List<JournalLine>();

    // Computed — مجموع المدين = مجموع الدائن
    public decimal TotalDebit  => Lines.Sum(l => l.Debit);
    public decimal TotalCredit => Lines.Sum(l => l.Credit);
    public bool    IsBalanced  => TotalDebit == TotalCredit;
}

public class JournalLine : BaseEntity
{
    public int           JournalEntryId { get; set; }
    public JournalEntry  JournalEntry   { get; set; } = null!;
    public int           AccountId      { get; set; }
    public Account       Account        { get; set; } = null!;
    public decimal       Debit          { get; set; } = 0;   // مدين
    public decimal       Credit         { get; set; } = 0;   // دائن
    public string?       Description    { get; set; }        // بيان السطر
    // روابط اختيارية
    public int?          CustomerId     { get; set; }
    public int?          SupplierId     { get; set; }
    public int?          OrderId        { get; set; }
}

// ══════════════════════════════════════════════════════
// سند قبض (Receipt Voucher)
// ══════════════════════════════════════════════════════

public enum VoucherPaymentMethod
{
    Cash         = 1,
    BankTransfer = 2,
    Check        = 3,
    Vodafone     = 4,
    InstaPay     = 5,
}

public class ReceiptVoucher : BaseEntity
{
    public string   VoucherNumber   { get; set; } = string.Empty;
    public DateTime VoucherDate     { get; set; } = DateTime.UtcNow;
    public decimal  Amount          { get; set; }
    public int      CashAccountId   { get; set; }   // حساب النقدية (مدين)
    public Account  CashAccount     { get; set; } = null!;
    public int      FromAccountId   { get; set; }   // الحساب الدائن (من مين)
    public Account  FromAccount     { get; set; } = null!;
    public int?     CustomerId      { get; set; }
    public Customer? Customer       { get; set; }
    public VoucherPaymentMethod PaymentMethod { get; set; } = VoucherPaymentMethod.Cash;
    public string?  Reference       { get; set; }   // رقم مرجعي
    public string?  Description     { get; set; }   // البيان
    public int?     JournalEntryId  { get; set; }
    public JournalEntry? JournalEntry { get; set; }
    public string?  CreatedByUserId { get; set; }
    public string?  AttachmentUrl   { get; set; }
    public string?  AttachmentPublicId { get; set; }
}

// ══════════════════════════════════════════════════════
// سند دفع / مصروف (Payment Voucher)
// ══════════════════════════════════════════════════════

public class PaymentVoucher : BaseEntity
{
    public string   VoucherNumber   { get; set; } = string.Empty;
    public DateTime VoucherDate     { get; set; } = DateTime.UtcNow;
    public decimal  Amount          { get; set; }
    public int      CashAccountId   { get; set; }   // حساب النقدية (دائن)
    public Account  CashAccount     { get; set; } = null!;
    public int      ToAccountId     { get; set; }   // الحساب المدين (لمين)
    public Account  ToAccount       { get; set; } = null!;
    public int?     SupplierId      { get; set; }
    public Supplier? Supplier       { get; set; }
    public VoucherPaymentMethod PaymentMethod { get; set; } = VoucherPaymentMethod.Cash;
    public string?  Reference       { get; set; }
    public string?  Description     { get; set; }   // البيان
    public int?     JournalEntryId  { get; set; }
    public JournalEntry? JournalEntry { get; set; }
    public string?  CreatedByUserId { get; set; }
    public string?  AttachmentUrl   { get; set; }
    public string?  AttachmentPublicId { get; set; }
}
