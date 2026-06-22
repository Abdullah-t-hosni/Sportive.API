using Sportive.API.Utils;
// ============================================================
// Services/AuditService.cs
// ✅ جديد — خدمة تسجيل التدقيق للعمليات الحساسة
// ============================================================
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Interfaces;

namespace Sportive.API.Services;

public interface IAuditService
{
    /// <summary>Log a simple action. AuditLogs are APPEND-ONLY — never update or delete.</summary>
    Task LogAsync(string action, string entityType, string? entityId = null,
        string? notes = null, string? userId = null, string? userName = null, string? ip = null);

    /// <summary>Log a change with old/new values. AuditLogs are APPEND-ONLY — never update or delete.</summary>
    Task LogChangeAsync<T>(string action, string entityType, string? entityId,
        T? oldValue, T? newValue, string? userId = null, string? userName = null, string? ip = null);

    /// <summary>Cleans up old audit logs permanently to save space</summary>
    Task CleanupOldLogsAsync(int monthsToKeep);
}

public class AuditService : IAuditService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditService> _logger;
    private readonly ITenantContext _tenantContext;
    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AuditService(IServiceScopeFactory scopeFactory, ILogger<AuditService> logger, ITenantContext tenantContext)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task LogAsync(
        string action, string entityType, string? entityId = null,
        string? notes = null, string? userId = null, string? userName = null, string? ip = null)
    {
        var rawTime = TimeHelper.GetEgyptTime();
        var createdAt = new DateTime(rawTime.Year, rawTime.Month, rawTime.Day, rawTime.Hour, rawTime.Minute, rawTime.Second, DateTimeKind.Unspecified);

        var log = new AuditLog
        {
            Action       = action,
            EntityType   = entityType,
            EntityId     = entityId,
            Notes        = notes,
            UserId       = userId,
            UserName     = userName,
            IpAddress    = ip,
            CreatedAt    = createdAt
        };

        var tenant = _tenantContext.CurrentTenant;
        if (tenant != null)
        {
            AuditQueueProcessor.EnqueueAuditLogs(new List<AuditLog> { log }, _scopeFactory, tenant);
        }
        await Task.CompletedTask;
    }

    public async Task LogChangeAsync<T>(
        string action, string entityType, string? entityId,
        T? oldValue, T? newValue,
        string? userId = null, string? userName = null, string? ip = null)
    {
        var rawTime = TimeHelper.GetEgyptTime();
        var createdAt = new DateTime(rawTime.Year, rawTime.Month, rawTime.Day, rawTime.Hour, rawTime.Minute, rawTime.Second, DateTimeKind.Unspecified);

        var log = new AuditLog
        {
            Action       = action,
            EntityType   = entityType,
            EntityId     = entityId,
            OldValues    = oldValue  != null ? JsonSerializer.Serialize(oldValue,  _jsonOpts) : null,
            NewValues    = newValue  != null ? JsonSerializer.Serialize(newValue,  _jsonOpts) : null,
            UserId       = userId,
            UserName     = userName,
            IpAddress    = ip,
            CreatedAt    = createdAt
        };

        var tenant = _tenantContext.CurrentTenant;
        if (tenant != null)
        {
            AuditQueueProcessor.EnqueueAuditLogs(new List<AuditLog> { log }, _scopeFactory, tenant);
        }
        await Task.CompletedTask;
    }

    public async Task CleanupOldLogsAsync(int monthsToKeep)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var cutoffDate = DateTime.UtcNow.AddMonths(-monthsToKeep);
            int deleted = await db.AuditLogs
                .Where(x => x.CreatedAt < cutoffDate)
                .ExecuteDeleteAsync();
                
            if (deleted > 0)
            {
                _logger.LogInformation($"Cleaned up {deleted} audit logs older than {monthsToKeep} months.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old audit logs.");
        }
    }
}
