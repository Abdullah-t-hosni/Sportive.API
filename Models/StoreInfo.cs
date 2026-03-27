using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sportive.API.Models;

[Table("StoreSettings")]
public class StoreInfo
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int StoreConfigId { get; set; } = 1;
    
    [MaxLength(100)]
    public string StoreBrandName { get; set; } = "Sportive";
    
    [MaxLength(200)]
    public string StoreSlogan { get; set; } = "Your Ultimate Sports Destination";
    
    [MaxLength(20)]
    public string StorePhoneNo { get; set; } = "";
    
    [MaxLength(20)]
    public string StoreWhatsAppNo { get; set; } = "";
    
    [MaxLength(100)]
    public string StoreEmailAddr { get; set; } = "";
    
    [MaxLength(500)]
    public string StorePhysicalAddr { get; set; } = "";
    
    public decimal VatRatePercent { get; set; } = 14;
    
    public decimal FixedDeliveryFee { get; set; } = 50;
    
    public decimal FreeDeliveryAt { get; set; } = 2000;
    
    [MaxLength(200)]
    public string FacebookPage { get; set; } = "";
    
    [MaxLength(200)]
    public string InstagramPage { get; set; } = "";
    
    [MaxLength(200)]
    public string TikTokPage { get; set; } = "";
    
    public bool InMaintenance { get; set; } = false;
    
    public DateTime LastUpdateDate { get; set; } = DateTime.UtcNow;
}
