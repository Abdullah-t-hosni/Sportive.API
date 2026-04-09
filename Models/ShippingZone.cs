using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Sportive.API.Models;

[Table("ShippingZones")]
public class ShippingZone
{
    [Key]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    [JsonPropertyName("nameAr")]
    public string NameAr { get; set; } = "";

    [Required]
    [MaxLength(100)]
    [JsonPropertyName("nameEn")]
    public string NameEn { get; set; } = "";

    /// <summary>
    /// Comma-separated list of governorates covered by this zone.
    /// Example: "Cairo, Giza, Alexandria"
    /// </summary>
    [Required]
    [JsonPropertyName("governorates")]
    public string Governorates { get; set; } = "";

    [JsonPropertyName("fee")]
    public decimal Fee { get; set; } = 50;

    [JsonPropertyName("freeThreshold")]
    public decimal? FreeThreshold { get; set; }

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = true;

    [MaxLength(50)]
    [JsonPropertyName("estimatedDays")]
    public string EstimatedDays { get; set; } = "2-5";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
