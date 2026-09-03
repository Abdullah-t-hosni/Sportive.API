using System;
using System.ComponentModel.DataAnnotations;

namespace Sportive.API.Models.System;

public class WhatsAppMessage
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(50)]
    public string Phone { get; set; } = null!;

    [MaxLength(200)]
    public string? CustomerName { get; set; }

    public string? Text { get; set; }

    public bool FromMe { get; set; }

    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [MaxLength(2000)]
    public string? MediaUrl { get; set; }

    [MaxLength(50)]
    public string? MediaType { get; set; }

    [MaxLength(200)]
    public string? FileName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
