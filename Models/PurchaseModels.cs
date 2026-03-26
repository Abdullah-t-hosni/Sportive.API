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

    // حساب تلقائي
    public decimal TotalPurchases { get; set; } = 0;
    public decimal TotalPaid      { get; set; } = 0;
    public decimal Balance        => TotalPurchases - TotalPaid; // المديونية

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
}

public class PurchaseInvoice : BaseEntity
{
    public string InvoiceNumber         { get; set; } = string.Empty; // رقم النظام
    public string? SupplierInvoiceNumber { get; set; }               // رقم فاتورة المورد
    public int    SupplierId            { get; set; }
    public Supplier Supplier            { get; set; } = null!;

    public PaymentTerms          PaymentTerms { get; set; } = PaymentTerms.Cash;
    public PurchaseInvoiceStatus Status       { get; set; } = PurchaseInvoiceStatus.Draft;

    public DateTime  InvoiceDate  { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate      { get; set; }

    public decimal SubTotal      { get; set; } = 0;
    public decimal TaxPercent    { get; set; } = 0;   // نسبة الضريبة %
    public decimal TaxAmount     { get; set; } = 0;   // مبلغ الضريبة
    public decimal TotalAmount   { get; set; } = 0;
    public decimal PaidAmount    { get; set; } = 0;
    public decimal RemainingAmount => TotalAmount - PaidAmount;

    public string? Notes { get; set; }

    public ICollection<PurchaseInvoiceItem> Items    { get; set; } = new List<PurchaseInvoiceItem>();
    public ICollection<SupplierPayment>     Payments { get; set; } = new List<SupplierPayment>();
}

public class PurchaseInvoiceItem : BaseEntity
{
    public int    PurchaseInvoiceId { get; set; }
    public PurchaseInvoice Invoice  { get; set; } = null!;

    public int?    ProductId   { get; set; }
    public Product? Product    { get; set; }
    public string  Description { get; set; } = string.Empty; // وصف الصنف
    public string? Unit        { get; set; }                  // الوحدة
    public int     Quantity    { get; set; } = 1;
    public decimal UnitCost    { get; set; } = 0;
    public decimal TotalCost   { get; set; } = 0;
}

// ══════════════════════════════════════════════════════
public enum PaymentMethod_Purchase
{
    Cash       = 1,  // نقدي
    BankTransfer = 2, // تحويل بنكي
    Check      = 3,  // شيك
    Other      = 4,  // أخرى
}

public class SupplierPayment : BaseEntity
{
    public string  PaymentNumber { get; set; } = string.Empty; // رقم السند
    public int     SupplierId    { get; set; }
    public Supplier Supplier     { get; set; } = null!;

    public int?            PurchaseInvoiceId { get; set; }    // اختياري — مرتبط بفاتورة
    public PurchaseInvoice? Invoice          { get; set; }

    public DateTime PaymentDate  { get; set; } = DateTime.UtcNow;
    public decimal  Amount       { get; set; } = 0;
    public PaymentMethod_Purchase PaymentMethod { get; set; } = PaymentMethod_Purchase.Cash;
    public string   AccountName  { get; set; } = string.Empty; // اسم الحساب (الخزينة / البنك)
    public string?  Notes        { get; set; }                  // البيان
    public string?  ReferenceNumber { get; set; }               // رقم مرجعي
    public string?  CreatedByUserId { get; set; }
}
