using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Sportive.API.Models;

[Table("StoreSettings")]
public class StoreInfo
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [JsonPropertyName("id")]
    public int StoreConfigId { get; set; } = 1;
    
    // --- 1. Brand & Identity ---
    [MaxLength(100)]
    [JsonPropertyName("storeName")]
    public string StoreBrandName { get; set; } = "Sportive";
    
    [MaxLength(200)]
    [JsonPropertyName("slogan")]
    public string StoreSlogan { get; set; } = "Beyond Performance";

    [MaxLength(10)]
    [JsonPropertyName("orderNumberPrefix")]
    public string OrderNumberPrefix { get; set; } = "SPT";

    [MaxLength(10)]
    [JsonPropertyName("currencyCode")]
    public string CurrencyCode { get; set; } = "EGP";

    [MaxLength(10)]
    [JsonPropertyName("currencySymbol")]
    public string CurrencySymbol { get; set; } = "ج.م";

    [MaxLength(500)]
    [JsonPropertyName("logoUrl")]
    public string? LogoUrl { get; set; }

    [MaxLength(500)]
    [JsonPropertyName("faviconUrl")]
    public string? FaviconUrl { get; set; }

    // --- 2. Appearance & UI ---
    [JsonPropertyName("brandColorH")]
    public int BrandColorH { get; set; } = 221; // Blue Hue

    [JsonPropertyName("brandColorS")]
    public int BrandColorS { get; set; } = 83; // Saturation

    [JsonPropertyName("brandColorL")]
    public int BrandColorL { get; set; } = 53; // Lightness

    [JsonPropertyName("announcementEnabled")]
    public bool AnnouncementEnabled { get; set; } = false;

    [MaxLength(500)]
    [JsonPropertyName("announcementText")]
    public string? AnnouncementText { get; set; }

    [MaxLength(200)]
    [JsonPropertyName("heroTitle")]
    public string? HeroTitle { get; set; }

    [MaxLength(500)]
    [JsonPropertyName("heroSubtitle")]
    public string? HeroSubtitle { get; set; }

    [MaxLength(1000)]
    [JsonPropertyName("heroImageUrl")]
    public string? HeroImageUrl { get; set; }

    // --- 3. Contact Information ---
    [MaxLength(20)]
    [JsonPropertyName("phone")]
    public string StorePhoneNo { get; set; } = "";
    
    [MaxLength(20)]
    [JsonPropertyName("whatsApp")]
    public string StoreWhatsAppNo { get; set; } = "";
    
    [MaxLength(100)]
    [JsonPropertyName("email")]
    public string StoreEmailAddr { get; set; } = "";
    
    [MaxLength(500)]
    [JsonPropertyName("address")]
    public string StorePhysicalAddr { get; set; } = "";
    
    // Social Media
    [MaxLength(500)]
    [JsonPropertyName("facebookUrl")]
    public string FacebookPage { get; set; } = "";
    
    [MaxLength(500)]
    [JsonPropertyName("instagramUrl")]
    public string InstagramPage { get; set; } = "";
    
    [MaxLength(500)]
    [JsonPropertyName("tikTokUrl")]
    public string TikTokPage { get; set; } = "";

    [MaxLength(500)]
    [JsonPropertyName("youtubeUrl")]
    public string? YoutubeUrl { get; set; }

    [MaxLength(500)]
    [JsonPropertyName("twitterUrl")]
    public string? TwitterUrl { get; set; }

    // --- 4. Sales & Orders ---
    [JsonPropertyName("minOrderAmount")]
    public decimal MinOrderAmount { get; set; } = 0;

    [JsonPropertyName("allowGuestCheckout")]
    public bool AllowGuestCheckout { get; set; } = true;

    [JsonPropertyName("enableCoupons")]
    public bool EnableCoupons { get; set; } = true;

    [JsonPropertyName("enableReviews")]
    public bool EnableReviews { get; set; } = true;

    [JsonPropertyName("reviewsRequirePurchase")]
    public bool ReviewsRequirePurchase { get; set; } = true;

    [MaxLength(500)]
    [JsonPropertyName("allowedPaymentMethods")]
    public string AllowedPaymentMethods { get; set; } = "Cash,Vodafone,InstaPay";

    [JsonPropertyName("receiptHeaderText")]
    public string? ReceiptHeaderText { get; set; }

    [JsonPropertyName("receiptFooterText")]
    public string? ReceiptFooterText { get; set; }

    [JsonPropertyName("receiptShowLogo")]
    public bool ReceiptShowLogo { get; set; } = true;

    [JsonPropertyName("receiptShowBarcode")]
    public bool ReceiptShowBarcode { get; set; } = true;

    [MaxLength(2000)]
    [JsonPropertyName("receiptTermsAndConditions")]
    public string? ReceiptTermsAndConditions { get; set; }

    [JsonPropertyName("receiptShowCustomerDetails")]
    public bool ReceiptShowCustomerDetails { get; set; } = true;

    [JsonPropertyName("receiptShowAddress")]
    public bool ReceiptShowAddress { get; set; } = true;

    [JsonPropertyName("receiptShowPhone")]
    public bool ReceiptShowPhone { get; set; } = true;

    [MaxLength(50)]
    [JsonPropertyName("receiptComplaintsPhone")]
    public string? ReceiptComplaintsPhone { get; set; }

    [JsonPropertyName("receiptShowTotalPieceCount")]
    public bool ReceiptShowTotalPieceCount { get; set; } = true;

    [JsonPropertyName("receiptShowItemCount")]
    public bool ReceiptShowItemCount { get; set; } = true;

    [JsonPropertyName("receiptShowBalance")]
    public bool ReceiptShowBalance { get; set; } = true;

    [MaxLength(200)]
    [JsonPropertyName("receiptSoftwareProvider")]
    public string? ReceiptSoftwareProvider { get; set; } = "By Easy Store";

    [MaxLength(20)]
    [JsonPropertyName("receiptPaperSize")]
    public string ReceiptPaperSize { get; set; } = "Receipt";

    [JsonPropertyName("receiptWidth")]
    public int ReceiptWidth { get; set; } = 80;

    [JsonPropertyName("receiptFontSize")]
    public int ReceiptFontSize { get; set; } = 11;

    [MaxLength(50)]
    [JsonPropertyName("receiptFontFamily")]
    public string ReceiptFontFamily { get; set; } = "Alexandria";

    [MaxLength(20)]
    [JsonPropertyName("receiptLogoPosition")]
    public string ReceiptLogoPosition { get; set; } = "center";

    [JsonPropertyName("receiptLogoWidth")]
    public int ReceiptLogoWidth { get; set; } = 80;

    [MaxLength(50)]
    [JsonPropertyName("orderStatusAfterPrint")]
    public string? OrderStatusAfterPrint { get; set; }

    [MaxLength(100)]
    [JsonPropertyName("qzReceiptPrinter")]
    public string? QzReceiptPrinter { get; set; }

    [MaxLength(100)]
    [JsonPropertyName("qzA4Printer")]
    public string? QzA4Printer { get; set; }

    [MaxLength(100)]
    [JsonPropertyName("qzBarcodePrinter")]
    public string? QzBarcodePrinter { get; set; }

    [MaxLength(10)]
    [JsonPropertyName("receiptLineStyle")]
    public string ReceiptLineStyle { get; set; } = "dashed";

    [JsonPropertyName("receiptDensity")]
    public int ReceiptDensity { get; set; } = 2;

    [JsonPropertyName("receiptBarcodeHeight")]
    public int ReceiptBarcodeHeight { get; set; } = 10;

    [MaxLength(1000)]
    [JsonPropertyName("receiptSectionsOrder")]
    public string ReceiptSectionsOrder { get; set; } = "header,order_info,items_table,totals_area,tafqeet,payment_info,footer_text,terms_conditions,barcode";

    // --- 5. Finance & VAT ---
    [JsonPropertyName("vatPercent")]
    public decimal VatRatePercent { get; set; } = 14;
    
    [JsonPropertyName("deliveryFee")]
    public decimal FixedDeliveryFee { get; set; } = 50;
    
    [JsonPropertyName("freeDeliveryThreshold")]
    public decimal FreeDeliveryAt { get; set; } = 2000;
    
    [JsonPropertyName("deliveryAccountId")]
    public string? DeliveryAccountId { get; set; } 

    [JsonPropertyName("deliveryRevenueAccountId")]
    public string? DeliveryRevenueAccountId { get; set; } 

    [JsonPropertyName("storeVatAccountId")]
    public string? StoreVatAccountId { get; set; } 

    // --- 6. Inventory Logic ---
    [JsonPropertyName("lowStockThreshold")]
    public int LowStockThreshold { get; set; } = 5;

    [JsonPropertyName("allowBackorders")]
    public bool AllowBackorders { get; set; } = false;

    [JsonPropertyName("hideOutOfStock")]
    public bool HideOutOfStock { get; set; } = false;
    
    // --- 7. System & Maintenance ---
    [JsonPropertyName("isMaintenanceMode")]
    public bool InMaintenance { get; set; } = false;

    [MaxLength(10)]
    [JsonPropertyName("backupTime")]
    public string BackupTime { get; set; } = "02:00"; 

    [JsonPropertyName("backupUtcOffset")]
    public int BackupUtcOffset { get; set; } = 2; 

    [MaxLength(100)]
    [JsonPropertyName("timeZoneId")]
    public string TimeZoneId { get; set; } = "Egypt Standard Time";

    [JsonPropertyName("accountingLockDate")]
    public DateTime? AccountingLockDate { get; set; }

    // --- 8. WhatsApp Quick Replies ---
    [JsonPropertyName("whatsAppOrderTemplate")]
    public string? WhatsAppOrderTemplate { get; set; } = "أهلاً {customerName}، تم استلام طلبك رقم #{orderNumber} وجاري التجهيز.";

    [JsonPropertyName("whatsAppShippingTemplate")]
    public string? WhatsAppShippingTemplate { get; set; } = "طلبك #{orderNumber} في الطريق مع المندوب، سيصلك قريباً.";

    [JsonPropertyName("whatsAppReturnTemplate")]
    public string? WhatsAppReturnTemplate { get; set; } = "تم استلام طلب المرتجع الخاص بك رقم #{orderNumber}، وجاري مراجعته.";

    // --- 9. Order Success Page ---
    [JsonPropertyName("orderSuccessMessageAr")]
    public string? OrderSuccessMessageAr { get; set; } = "شكراً لتسوقك معنا! سيقوم فريقنا بالتواصل معك قريباً لتأكيد الطلب.";

    [JsonPropertyName("orderSuccessMessageEn")]
    public string? OrderSuccessMessageEn { get; set; } = "Thank you for shopping with us! Our team will contact you soon to confirm your order.";

    // --- 10. Smart Printing & POS ---
    [JsonPropertyName("autoPrintReceipt")]
    public bool AutoPrintReceipt { get; set; } = false;

    [JsonPropertyName("receiptExtraCopies")]
    public int ReceiptExtraCopies { get; set; } = 0;

    // --- 11. SEO & Analytics ---
    [JsonPropertyName("googleAnalyticsId")]
    public string? GoogleAnalyticsId { get; set; }

    [JsonPropertyName("facebookPixelId")]
    public string? FacebookPixelId { get; set; }

    [JsonPropertyName("siteMetaDescriptionAr")]
    public string? SiteMetaDescriptionAr { get; set; }

    [JsonPropertyName("siteMetaDescriptionEn")]
    public string? SiteMetaDescriptionEn { get; set; }

    [JsonPropertyName("siteKeywords")]
    public string? SiteKeywords { get; set; }

    // --- 12. Barcode Customization ---
    [JsonPropertyName("barcodeShowPrice")]
    public bool BarcodeShowPrice { get; set; } = true;

    [JsonPropertyName("barcodeShowName")]
    public bool BarcodeShowName { get; set; } = true;

    [JsonPropertyName("barcodeShowSize")]
    public bool BarcodeShowSize { get; set; } = true;

    [JsonPropertyName("barcodeShowColor")]
    public bool BarcodeShowColor { get; set; } = true;

    [JsonPropertyName("lastUpdatedAt")]
    public DateTime LastUpdateDate { get; set; } = DateTime.UtcNow;
}
