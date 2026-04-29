using Sportive.API.Utils;
// ============================================================
// Services/AuditService.cs
// ✅ جديد — خدمة تسجيل التدقيق للعمليات الحساسة
// ============================================================
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Services;

public interface IAuditService
{
    /// <summary>Log a simple action. AuditLogs are APPEND-ONLY — never update or delete.</summary>
    Task LogAsync(string action, string entityType, string? entityId = null,
        string? notes = null, string? userId = null, string? userName = null, string? ip = null);

    /// <summary>Log a change with old/new values. AuditLogs are APPEND-ONLY — never update or delete.</summary>
    Task LogChangeAsync<T>(string action, string entityType, string? entityId,
        T? oldValue, T? newValue, string? userId = null, string? userName = null, string? ip = null);
}

public class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AuditService(AppDbContext db) => _db = db;

    public async Task LogAsync(
        string action, string entityType, string? entityId = null,
        string? notes = null, string? userId = null, string? userName = null, string? ip = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Action     = action,
            EntityType = entityType,
            EntityId   = entityId,
            Notes      = notes,
            UserId     = userId,
            UserName   = userName,
            IpAddress  = ip,
            CreatedAt  = TimeHelper.GetEgyptTime()
        });
        await _db.SaveChangesAsync();
    }

    public async Task LogChangeAsync<T>(
        string action, string entityType, string? entityId,
        T? oldValue, T? newValue,
        string? userId = null, string? userName = null, string? ip = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Action     = action,
            EntityType = entityType,
            EntityId   = entityId,
            OldValues  = oldValue  != null ? JsonSerializer.Serialize(oldValue,  _jsonOpts) : null,
            NewValues  = newValue  != null ? JsonSerializer.Serialize(newValue,  _jsonOpts) : null,
            UserId     = userId,
            UserName   = userName,
            IpAddress  = ip,
            CreatedAt  = TimeHelper.GetEgyptTime()
        });
        await _db.SaveChangesAsync();
    }
}
