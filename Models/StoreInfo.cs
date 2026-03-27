using System.ComponentModel.DataAnnotations;

namespace Sportive.API.Models;

public class StoreInfo
{
    public int Id { get; set; } = 1;
    
    [MaxLength(100)]
    public string StoreName { get; set; } = "Sportive";
    
    [MaxLength(200)]
    public string Slogan { get; set; } = "Your Ultimate Sports Destination";
    
    [MaxLength(20)]
    public string Phone { get; set; } = "";
    
    [MaxLength(20)]
    public string WhatsApp { get; set; } = "";
    
    [MaxLength(100)]
    public string Email { get; set; } = "";
    
    [MaxLength(500)]
    public string Address { get; set; } = "";
    
    public decimal VatPercent { get; set; } = 14;
    
    public decimal DeliveryFee { get; set; } = 50;
    
    public decimal FreeDeliveryThreshold { get; set; } = 2000;
    
    [MaxLength(200)]
    public string FacebookUrl { get; set; } = "";
    
    [MaxLength(200)]
    public string InstagramUrl { get; set; } = "";
    
    [MaxLength(200)]
    public string TikTokUrl { get; set; } = "";
    
    public bool IsMaintenanceMode { get; set; } = false;
    
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}
