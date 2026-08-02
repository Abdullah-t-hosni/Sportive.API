namespace Sportive.API.Models;

public enum ShippingIntegrationType
{
    Manual = 1, // شحن عادي / يدوي
    Bosta = 2   // مربوط برمجياً مع بوسطة
}

public class ShippingCompany : BaseEntity
{
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? ContactInfo { get; set; }
    
    public ShippingIntegrationType IntegrationType { get; set; } = ShippingIntegrationType.Manual;
    
    // الربط المحاسبي
    public int? AccountId { get; set; }
    public Account? Account { get; set; }
    
    public bool IsActive { get; set; } = true;
}
