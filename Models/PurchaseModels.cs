using Sportive.API.Utils;

namespace Sportive.API.Models;

// ══════════════════════════════════════════════════════
// Models/Supplier.cs
// ══════════════════════════════════════════════════════

public class Supplier : BaseEntity
{
    public string Name        { get; set; } = string.Empty; // اسم المورد — إلزامي
    public string Phone       { get; set; } = string.Empty; // تليفون — إلزامي
    public string? CompanyName  { get; set; }               // اسم الشركة
    public string? TaxNumber    { get; set; }               // الرقم الضريبي
    public string? Email        { get; set; }               // البريد الإلكتروني
    public string? Address      { get; set; }               // العنوان
    public bool   IsActive      { get; set; } = true;
    public decimal OpeningBalance { get; set; } = 0;

    // حساب تلقائي
    public decimal TotalPurchases { get; set; } = 0;
    public decimal TotalPaid      { get; set; } = 0;
    public decimal Balance        => OpeningBalance + TotalPurchases - TotalPaid; // المديونية (رصيد أول + مشتريات - مدفوعات)
    public string? AttachmentUrl { get; set; }
    public string? AttachmentPublicId { get; set; }

    public int? MainAccountId { get; set; } // الربط المحاسبي الدائم
    public Account? MainAccount { get; set; }

    public ICollection<PurchaseInvoice>  Invoices { get; set; } = new List<PurchaseInvoice>();
    public ICollection<SupplierPayment>  Payments { get; set; } = new List<SupplierPayment>();
}

// ══════════════════════════════════════════════════════
public enum PaymentTerms
{
    Cash   = 1,  // نقدي
    Credit = 2,  // آجل
}

public enum PurchaseInvoiceStatus
{
    Draft    = 1,  // مسودة
    Received = 2,  // مستلمة
    Paid     = 3,  // مدفوعة
    PartPaid = 4,  // مدفوعة جزئياً
    Overdue  = 5,  // متأخرة
    Returned = 6,  // مرتجعة كلياً
    Cancelled = 7, // ملغاة
    PartiallyReturned = 8 // مرتجعة جزئياً
}

public class PurchaseInvoice : BaseEntity
{
    public string InvoiceNumber         { get; set; } = string.Empty; // رقم النظام
    public string? SupplierInvoiceNumber { get; set; }               // رقم فاتورة المورد
    public int    SupplierId            { get; set; }
    public Supplier Supplier            { get; set; } = null!;

    public PaymentTerms          PaymentTerms { get; set; } = PaymentTerms.Cash;
    public PurchaseInvoiceStatus Status       { get; set; } = PurchaseInvoiceStatus.Draft;

    public DateTime  InvoiceDate  { get; set; } = TimeHelper.GetEgyptTime();
    public int?      PaymentTermDays { get; set; }                   // دورة الدفع بالأيام (30، 60، إلخ)
    public DateTime? DueDate      { get; set; }

    public decimal SubTotal      { get; set; } = 0;
    public decimal TaxPercent    { get; set; } = 0;   // نسبة الضريبة %
    public decimal TaxAmount     { get; set; } = 0;   // مبلغ الضريبة
    public decimal DiscountAmount { get; set; } = 0;  // قيمة الخصم المكتسب
    public decimal TotalAmount   { get; set; } = 0; // الإجمالي النهائي
    public decimal PaidAmount    { get; set; } = 0; // المبلغ المدفوع
    public decimal ReturnedAmount { get; set; } = 0; // القيمة المرتجعة
    public decimal RemainingAmount => TotalAmount - PaidAmount - ReturnedAmount;

    public string? Notes { get; set; }
    public string? AttachmentUrl { get; set; }
    public string? AttachmentPublicId { get; set; }

    // ── FINANCIAL MAPPINGS (Explicit per invoice) ───────
    public int? VendorAccountId    { get; set; } // الحساب الدائن (المورد)
    public int? InventoryAccountId { get; set; } // الحساب المدين (المخزون)
    public int? ExpenseAccountId   { get; set; } // الحساب المدين (المشتريات)
    public int? VatAccountId       { get; set; } // حساب الضريبة
    public int? CashAccountId      { get; set; } // حساب الخزينة (لو نقدي)
    public OrderSource? CostCenter   { get; set; } // مركز التكلفة (Website/POS)

    public ICollection<PurchaseInvoiceItem> Items    { get; set; } = new List<PurchaseInvoiceItem>();
    public ICollection<SupplierPayment>     Payments { get; set; } = new List<SupplierPayment>();
    public ICollection<JournalEntry>        JournalEntries { get; set; } = new List<JournalEntry>();
}

public class PurchaseInvoiceItem : BaseEntity
{
    public int    PurchaseInvoiceId { get; set; }
    public PurchaseInvoice Invoice  { get; set; } = null!;

    public int?    ProductId        { get; set; }
    public Product? Product         { get; set; }
    public int?    ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }
    
    public string  Description { get; set; } = string.Empty; // وصف الصنف
    public string? Unit        { get; set; }                  // الوحدة
    public decimal Quantity    { get; set; } = 1;
    public decimal UnitCost    { get; set; } = 0;
    public decimal TotalCost   { get; set; } = 0;
    
    public decimal ReturnedQuantity { get; set; } = 0;
}

// ══════════════════════════════════════════════════════
public enum PaymentMethod_Purchase
{
    Cash         = 1,  // نقدي
    BankTransfer = 2,  // تحويل بنكي
    Check        = 3,  // شيك
    Vodafone     = 4,  // فودافون كاش
    InstaPay     = 5,  // انستاباي
    Other        = 6,  // أخرى
}

public class SupplierPayment : BaseEntity
{
    public string  PaymentNumber { get; set; } = string.Empty; // رقم السند
    public int     SupplierId    { get; set; }
    public Supplier Supplier     { get; set; } = null!;

    public int?            PurchaseInvoiceId { get; set; }    // اختياري — مرتبط بفاتورة
    public PurchaseInvoice? Invoice          { get; set; }

    public DateTime PaymentDate  { get; set; } = TimeHelper.GetEgyptTime();
    public decimal  Amount       { get; set; } = 0;
    public PaymentMethod_Purchase PaymentMethod { get; set; } = PaymentMethod_Purchase.Cash;
    public string   AccountName  { get; set; } = string.Empty; // اسم الحساب (الخزينة / البنك)
    public string?  Notes        { get; set; }                  // البيان
    public string?  ReferenceNumber { get; set; }               // رقم مرجعي
    public int?     CashAccountId   { get; set; }               // الربط المحاسبي (خزينة/بنك)
    public Account? CashAccount      { get; set; }
    public string?  CreatedByUserId { get; set; }
    public string?  AttachmentUrl   { get; set; }
    public string?  AttachmentPublicId { get; set; }
    public OrderSource? CostCenter  { get; set; } // مركز التكلفة (Website/POS)
}

// ══════════════════════════════════════════════════════
// PURCHASE RETURNS
// ══════════════════════════════════════════════════════

public class PurchaseReturn : BaseEntity
{
    public string ReturnNumber { get; set; } = string.Empty; // رقم مستند المرتجع (PR-xxxx)
    public int? PurchaseInvoiceId { get; set; }
    public PurchaseInvoice? Invoice { get; set; }

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public DateTime ReturnDate { get; set; } = TimeHelper.GetEgyptTime();
    public decimal SubTotal { get; set; } = 0;
    public decimal TaxAmount { get; set; } = 0;
    public decimal DiscountAmount { get; set; } = 0;
    public decimal TotalAmount { get; set; } = 0; // المبلغ الإجمالي المرتجع (يخصم من مديونية المورد)

    public string? Notes { get; set; }
    public string? ReferenceNumber { get; set; } // رقم مرجعي خارجي
    public string? CreatedByUserId { get; set; }

    public PaymentTerms PaymentTerms { get; set; } = PaymentTerms.Credit;
    public int? CashAccountId { get; set; }
    public Account? CashAccount { get; set; }
    public OrderSource? CostCenter  { get; set; } // مركز التكلفة (Website/POS)

    public ICollection<PurchaseReturnItem> Items { get; set; } = new List<PurchaseReturnItem>();
}

public class PurchaseReturnItem : BaseEntity
{
    public int PurchaseReturnId { get; set; }
    public PurchaseReturn PurchaseReturn { get; set; } = null!;

    public int? PurchaseInvoiceItemId { get; set; }
    public PurchaseInvoiceItem? InvoiceItem { get; set; }

    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public decimal Quantity { get; set; } = 0; // الكمية المرتجعة في هذه العملية
    public string? Unit { get; set; } // الوحدة المستخدمة في المرتجع
    public decimal UnitCost { get; set; } = 0; // تكلفة الوحدة وقت الشراء
    public decimal TotalCost { get; set; } = 0;
}

