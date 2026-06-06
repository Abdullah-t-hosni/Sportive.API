using System;
using System.ComponentModel.DataAnnotations;
using Sportive.API.Utils;

namespace Sportive.API.Models;

public enum SecurityEventType
{
    FailedLogin,
    RateLimitViolation,
    AccessDenied
}

public enum SecuritySeverity
{
    Low,
    Medium,
    High,
    Critical
}

public class SecurityEvent
{
    public int Id { get; set; }
    
    [MaxLength(100)]
    public string? UserId { get; set; }
    public AppUser? User { get; set; }

    [MaxLength(50)]
    public string IpAddress { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Device { get; set; } = string.Empty;

    public SecurityEventType EventType { get; set; }
    public SecuritySeverity Severity { get; set; }
    
    public int RiskScore { get; set; }

    [MaxLength(100)]
    public string CorrelationId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = TimeHelper.GetEgyptTime();
}
