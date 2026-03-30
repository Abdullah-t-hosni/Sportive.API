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
    
    [MaxLength(100)]
    [JsonPropertyName("storeName")]
    public string StoreBrandName { get; set; } = "Sportive";
    
    [MaxLength(200)]
    [JsonPropertyName("slogan")]
    public string StoreSlogan { get; set; } = "Your Ultimate Sports Destination";
    
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
    
    [JsonPropertyName("vatPercent")]
    public decimal VatRatePercent { get; set; } = 14;
    
    [JsonPropertyName("deliveryFee")]
    public decimal FixedDeliveryFee { get; set; } = 50;
    
    [JsonPropertyName("freeDeliveryThreshold")]
    public decimal FreeDeliveryAt { get; set; } = 2000;
    
    [JsonPropertyName("deliveryAccountId")]
    public string? DeliveryAccountId { get; set; } // الربط المالي: حساب إيراد التوصيل
    
    [JsonPropertyName("storeVatAccountId")]
    public string? StoreVatAccountId { get; set; } // الربط المالي: حساب ضريبة المبيعات
    
    [MaxLength(200)]
    [JsonPropertyName("facebookUrl")]
    public string FacebookPage { get; set; } = "";
    
    [MaxLength(200)]
    [JsonPropertyName("instagramUrl")]
    public string InstagramPage { get; set; } = "";
    
    [MaxLength(200)]
    [JsonPropertyName("tiktokUrl")]
    public string TikTokPage { get; set; } = "";
    
    [JsonPropertyName("isMaintenanceMode")]
    public bool InMaintenance { get; set; } = false;
    
    [JsonPropertyName("lastUpdatedAt")]
    public DateTime LastUpdateDate { get; set; } = DateTime.UtcNow;
}
