using System;
using Sportive.API.Utils;

namespace Sportive.API.Models;

public class UserSession
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public AppUser? User { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceFingerprint { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = TimeHelper.GetEgyptTime();
    public DateTime LastSeen { get; set; } = TimeHelper.GetEgyptTime();
    public DateTime? ExpiresAt { get; set; }
    public string RefreshTokenHash { get; set; } = string.Empty;
    public bool IsRevoked { get; set; } = false;
    public DateTime? RevokedAt { get; set; }
}
