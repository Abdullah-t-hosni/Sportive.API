namespace Sportive.API.Models;

/// <summary>
/// جدول موحد لتخزين المرفقات المتعددة لأي entity في النظام
/// يدعم: order, purchase, journalentry, assetpurchase
/// </summary>
public class EntityAttachment : BaseEntity
{
    /// <summary>نوع الـ entity: order | purchase | journalentry | assetpurchase</summary>
    public string EntityType        { get; set; } = string.Empty;

    /// <summary>رقم الـ entity (Order.Id, PurchaseInvoice.Id, JournalEntry.Id ...)</summary>
    public int    EntityId          { get; set; }

    /// <summary>رابط الملف (Cloudinary أو Local)</summary>
    public string Url               { get; set; } = string.Empty;

    /// <summary>Public ID للحذف من Cloudinary</summary>
    public string? PublicId         { get; set; }

    /// <summary>اسم الملف الأصلي</summary>
    public string? FileName         { get; set; }

    /// <summary>نوع الملف (image/jpeg, image/png, application/pdf ...)</summary>
    public string? ContentType      { get; set; }

    /// <summary>حجم الملف بالبايت</summary>
    public long?   FileSizeBytes    { get; set; }

    /// <summary>ID المستخدم اللي رفع الملف</summary>
    public string? UploadedByUserId { get; set; }
}
