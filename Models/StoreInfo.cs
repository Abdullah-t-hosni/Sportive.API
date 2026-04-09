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

    [JsonPropertyName("lastUpdatedAt")]
    public DateTime LastUpdateDate { get; set; } = DateTime.UtcNow;
}
