using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Services
{
    public static class AuditQueueProcessor
    {
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public static void EnqueueAuditLogs(List<AuditLog> logs, IServiceScopeFactory scopeFactory)
        {
            if (logs == null || logs.Count == 0) return;

            // Fire and forget
            _ = Task.Run(async () =>
            {
                await _semaphore.WaitAsync();
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.BypassAuditLogging = true; // Disable recursive logging

                    // 1. Get the last record's hash
                    var lastRecord = await db.AuditLogs.OrderByDescending(x => x.Id).FirstOrDefaultAsync();
                    var previousHash = lastRecord?.Hash ?? "GENESIS";

                    // 2. Resolve Configuration for Secret
                    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                    var secret = config["Security:AuditSecret"];
                    if (string.IsNullOrEmpty(secret) || secret == "${AUDIT_SECRET}")
                    {
                        secret = Environment.GetEnvironmentVariable("AUDIT_SECRET");
                    }
                    if (string.IsNullOrEmpty(secret))
                    {
                        secret = "DefaultSecretKeyForAuditingPleaseChangeThisInProduction";
                    }

                    // 3. Compute hash and add each record
                    foreach (var log in logs)
                    {
                        log.PreviousHash = previousHash;
                        var payload = $"{log.Action}{log.UserId ?? ""}{log.CreatedAt:yyyy-MM-dd HH:mm:ss}{previousHash}";
                        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
                        var hashBytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
                        log.Hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                        db.AuditLogs.Add(log);
                        previousHash = log.Hash;
                    }

                    // 4. Save to DB
                    await db.SaveChangesAsync();

                    // 5. Trigger notifications if any of the logs are critical
                    var notificationService = scope.ServiceProvider.GetService<INotificationService>();
                    if (notificationService != null)
                    {
                        foreach (var log in logs)
                        {
                            await CheckAndTriggerAlertAsync(notificationService, log.Action, log.EntityType, log.EntityId, log.UserName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AuditQueueProcessor] Critical Error processing audit logs: {ex}");
                }
                finally
                {
                    _semaphore.Release();
                }
            });
        }

        private static async Task CheckAndTriggerAlertAsync(INotificationService notificationService, string action, string entityType, string? entityId, string? userName)
        {
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
                try
                {
                    await notificationService.SendAsync(
                        userId: null,
                        titleAr: alertTitleAr,
                        titleEn: alertTitleEn,
                        msgAr: alertMsgAr,
                        msgEn: alertMsgEn,
                        type: "Alert"
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AuditQueueProcessor] Failed to send critical notification: {ex}");
                }
            }
        }
    }
}
