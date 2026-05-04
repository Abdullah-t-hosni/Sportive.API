using System.ComponentModel.DataAnnotations;

namespace Sportive.API.Models
{
    public class OutboxMessage : BaseEntity
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public int TenantId { get; set; } = 1;

        [Required]
        public string EventType { get; set; } = string.Empty;
        
        [Required]
        public string Payload { get; set; } = string.Empty; // JSON
        
        public DateTime? ProcessedAt { get; set; }
        
        public string? Error { get; set; }
        
        public int RetryCount { get; set; } = 0;
    }
}
