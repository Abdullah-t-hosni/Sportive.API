using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sportive.API.Models;
using System.Text.Json;

namespace Sportive.API.Data;

public class AuditEntry
{
    public EntityEntry Entry { get; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? IpAddress { get; set; }
    public Dictionary<string, object?> OldValues { get; } = new();
    public Dictionary<string, object?> NewValues { get; } = new();
    public List<PropertyEntry> TemporaryProperties { get; } = new();

    public bool HasTemporaryProperties => TemporaryProperties.Any();

    public AuditEntry(EntityEntry entry)
    {
        Entry = entry;
    }

    public AuditLog ToAuditLog(string previousHash)
    {
        var rawTime = Sportive.API.Utils.TimeHelper.GetEgyptTime();
        var createdAt = new DateTime(rawTime.Year, rawTime.Month, rawTime.Day, rawTime.Hour, rawTime.Minute, rawTime.Second, DateTimeKind.Unspecified);

        var payload = $"{Action}{UserId ?? ""}{createdAt:yyyy-MM-dd HH:mm:ss}{previousHash}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(GetAuditSecret()));
        var hashBytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        return new AuditLog
        {
            Action = Action,
            EntityType = EntityType,
            EntityId = EntityId,
            OldValues = OldValues.Count == 0 ? null : JsonSerializer.Serialize(OldValues, jsonOpts),
            NewValues = NewValues.Count == 0 ? null : JsonSerializer.Serialize(NewValues, jsonOpts),
            UserId = UserId,
            UserName = UserName,
            IpAddress = IpAddress,
            CreatedAt = createdAt,
            PreviousHash = previousHash,
            Hash = hash,
            Notes = $"Auto-logged {Action} for {EntityType}"
        };
    }

    public AuditLog ToDraftAuditLog()
    {
        var rawTime = Sportive.API.Utils.TimeHelper.GetEgyptTime();
        var createdAt = new DateTime(rawTime.Year, rawTime.Month, rawTime.Day, rawTime.Hour, rawTime.Minute, rawTime.Second, DateTimeKind.Unspecified);

        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        return new AuditLog
        {
            Action = Action,
            EntityType = EntityType,
            EntityId = EntityId,
            OldValues = OldValues.Count == 0 ? null : JsonSerializer.Serialize(OldValues, jsonOpts),
            NewValues = NewValues.Count == 0 ? null : JsonSerializer.Serialize(NewValues, jsonOpts),
            UserId = UserId,
            UserName = UserName,
            IpAddress = IpAddress,
            CreatedAt = createdAt,
            Notes = $"Auto-logged {Action} for {EntityType}"
        };
    }

    private string GetAuditSecret()
    {
        var secret = Environment.GetEnvironmentVariable("AUDIT_SECRET");
        if (string.IsNullOrEmpty(secret))
        {
            // Fallback for development if needed, though production should have the environment variable
            secret = "DefaultSecretKeyForAuditingPleaseChangeThisInProduction";
        }
        return secret;
    }
}
