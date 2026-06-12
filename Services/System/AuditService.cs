using Sportive.API.Utils;
// ============================================================
// Services/AuditService.cs
// ✅ جديد — خدمة تسجيل التدقيق للعمليات الحساسة
// ============================================================
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    private readonly INotificationService _notificationService;
    private readonly string _auditSecret;
    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AuditService(AppDbContext db, INotificationService notificationService, IConfiguration config)
    {
        _db = db;
        _notificationService = notificationService;
        var secret = config["Security:AuditSecret"];
        if (string.IsNullOrEmpty(secret) || secret == "${AUDIT_SECRET}")
        {
            secret = Environment.GetEnvironmentVariable("AUDIT_SECRET");
        }
        _auditSecret = secret ?? string.Empty;
    }

    public async Task LogAsync(
        string action, string entityType, string? entityId = null,
        string? notes = null, string? userId = null, string? userName = null, string? ip = null)
    {
        var rawTime = TimeHelper.GetEgyptTime();
        // Truncate to whole seconds to avoid database-specific fractional second rounding mismatches
        var createdAt = new DateTime(rawTime.Year, rawTime.Month, rawTime.Day, rawTime.Hour, rawTime.Minute, rawTime.Second, DateTimeKind.Unspecified);

        var lastRecord = await _db.AuditLogs.OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        var previousHash = lastRecord?.Hash ?? "GENESIS";
        var hash = ComputeAuditHash(action, userId, createdAt, previousHash);

        _db.AuditLogs.Add(new AuditLog
        {
            Action       = action,
            EntityType   = entityType,
            EntityId     = entityId,
            Notes        = notes,
            UserId       = userId,
            UserName     = userName,
            IpAddress    = ip,
            CreatedAt    = createdAt,
            PreviousHash = previousHash,
            Hash         = hash
        });
        await _db.SaveChangesAsync();

        await CheckAndTriggerAlertAsync(action, entityType, entityId, userName);
    }

    public async Task LogChangeAsync<T>(
        string action, string entityType, string? entityId,
        T? oldValue, T? newValue,
        string? userId = null, string? userName = null, string? ip = null)
    {
        var rawTime = TimeHelper.GetEgyptTime();
        // Truncate to whole seconds to avoid database-specific fractional second rounding mismatches
        var createdAt = new DateTime(rawTime.Year, rawTime.Month, rawTime.Day, rawTime.Hour, rawTime.Minute, rawTime.Second, DateTimeKind.Unspecified);

        var lastRecord = await _db.AuditLogs.OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        var previousHash = lastRecord?.Hash ?? "GENESIS";
        var hash = ComputeAuditHash(action, userId, createdAt, previousHash);

        _db.AuditLogs.Add(new AuditLog
        {
            Action       = action,
            EntityType   = entityType,
            EntityId     = entityId,
            OldValues    = oldValue  != null ? JsonSerializer.Serialize(oldValue,  _jsonOpts) : null,
            NewValues    = newValue  != null ? JsonSerializer.Serialize(newValue,  _jsonOpts) : null,
            UserId       = userId,
            UserName     = userName,
            IpAddress    = ip,
            CreatedAt    = createdAt,
            PreviousHash = previousHash,
            Hash         = hash
        });
        await _db.SaveChangesAsync();

        await CheckAndTriggerAlertAsync(action, entityType, entityId, userName);
    }

    private async Task CheckAndTriggerAlertAsync(string action, string entityType, string? entityId, string? userName)
    {
        // Critical Actions that require SuperAdmin Notification
        var isCritical = false;
        var alertTitleEn = "Security Alert";
        var alertTitleAr = "تنبيه أمني";
        var alertMsgEn = "";
        var alertMsgAr = "";

        if (action.Contains("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            if (entityType == "JournalEntry" || entityType == "Account" || entityType == "PaymentVoucher" || entityType == "ReceiptVoucher" || entityType == "AuditLog")
            {
                isCritical = true;
                alertMsgEn = $"Critical Deletion: {entityType} ({entityId}) deleted by {userName}.";
                alertMsgAr = $"حذف حرج: تم حذف {entityType} ({entityId}) بواسطة {userName}.";
            }
        }
        else if (action.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) && entityType == "OpeningBalance")
        {
            isCritical = true;
            alertMsgEn = $"Opening Balance modified by {userName}.";
            alertMsgAr = $"تعديل رصيد افتتاحي بواسطة {userName}.";
        }

        if (isCritical)
        {
            // Send to SuperAdmins (userId null, but Type Alert broadcasts to Admins)
            await _notificationService.SendAsync(
                userId: null,
                titleAr: alertTitleAr,
                titleEn: alertTitleEn,
                msgAr: alertMsgAr,
                msgEn: alertMsgEn,
                type: "Alert"
            );
        }
    }

    private string ComputeAuditHash(string action, string? userId, DateTime createdAt, string previousHash)
    {
        // Format to ISO 8601 representation (unspecified kind/Egypt local)
        var payload = $"{action}{userId ?? ""}{createdAt:yyyy-MM-dd HH:mm:ss}{previousHash}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(_auditSecret));
        var hashBytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}
