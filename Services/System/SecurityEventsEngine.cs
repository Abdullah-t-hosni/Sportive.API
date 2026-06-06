using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Utils;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Sportive.API.Services;

public class SecurityEventsEngine : ISecurityEventsEngine
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<SecurityEventsEngine> _logger;

    public SecurityEventsEngine(
        AppDbContext db,
        IEmailService email,
        IMemoryCache cache,
        IConfiguration config,
        ILogger<SecurityEventsEngine> logger)
    {
        _db = db;
        _email = email;
        _cache = cache;
        _config = config;
        _logger = logger;
    }

    public async Task TrackEventAsync(
        string? userId, 
        string ipAddress, 
        string userAgent, 
        SecurityEventType eventType, 
        SecuritySeverity severity, 
        int riskScore, 
        string correlationId)
    {
        try
        {
            var egyptTime = TimeHelper.GetEgyptTime();

            // 1. Check for 20 Failed Logins in 1 minute on this IP
            if (eventType == SecurityEventType.FailedLogin)
            {
                var oneMinuteAgo = egyptTime.AddMinutes(-1);
                var failedLoginsLastMinute = await _db.SecurityEvents
                    .CountAsync(e => e.IpAddress == ipAddress && 
                                     e.EventType == SecurityEventType.FailedLogin && 
                                     e.CreatedAt >= oneMinuteAgo);

                if (failedLoginsLastMinute >= 19) // 19 existing + current 1 = 20
                {
                    severity = SecuritySeverity.Critical;
                    riskScore = 100;
                    _logger.LogCritical("[SecurityEngine] BRUTE FORCE DETECTED: 20 failed login attempts in 1 minute from IP: {IP}. CorrelationId: {CorrelationId}", ipAddress, correlationId);
                }
            }

            // 2. Save event to DB
            var securityEvent = new SecurityEvent
            {
                UserId = userId,
                IpAddress = ipAddress,
                Device = userAgent,
                EventType = eventType,
                Severity = severity,
                RiskScore = riskScore,
                CorrelationId = correlationId,
                CreatedAt = egyptTime
            };

            _db.SecurityEvents.Add(securityEvent);
            await _db.SaveChangesAsync();

            // 3. Compute cumulative risk scores in the last 24 hours
            var oneDayAgo = egyptTime.AddDays(-1);
            
            var ipCumulativeScore = await _db.SecurityEvents
                .Where(e => e.IpAddress == ipAddress && e.CreatedAt >= oneDayAgo)
                .SumAsync(e => e.RiskScore);

            var userCumulativeScore = 0;
            if (!string.IsNullOrEmpty(userId))
            {
                userCumulativeScore = await _db.SecurityEvents
                    .Where(e => e.UserId == userId && e.CreatedAt >= oneDayAgo)
                    .SumAsync(e => e.RiskScore);
            }

            // 4. Handle alerts if cumulative score >= 100
            if (ipCumulativeScore >= 100 || userCumulativeScore >= 100)
            {
                await EvaluateAndFireAlertsAsync(userId, ipAddress, ipCumulativeScore, userCumulativeScore, correlationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SecurityEngine] Error tracking security event");
        }
    }

    private async Task EvaluateAndFireAlertsAsync(string? userId, string ipAddress, int ipScore, int userScore, string correlationId)
    {
        var adminEmail = _config["Email:AdminEmail"] ?? _config["Store:Email"] ?? "admin@sportive.com";

        // Throttled alert cache keys
        var ipCacheKey = $"SecAlert_IP_{ipAddress}";
        var userCacheKey = userId != null ? $"SecAlert_User_{userId}" : null;

        bool shouldAlert = false;
        string reason = "";

        if (ipScore >= 100 && !_cache.TryGetValue(ipCacheKey, out _))
        {
            shouldAlert = true;
            reason += $"Cumulative IP risk score for {ipAddress} reached {ipScore}. ";
            _cache.Set(ipCacheKey, true, TimeSpan.FromHours(1));
        }

        if (userId != null && userScore >= 100 && userCacheKey != null && !_cache.TryGetValue(userCacheKey, out _))
        {
            shouldAlert = true;
            reason += $"Cumulative User risk score for UserId {userId} reached {userScore}. ";
            _cache.Set(userCacheKey, true, TimeSpan.FromHours(1));
        }

        if (shouldAlert)
        {
            _logger.LogWarning("[SecurityEngine] Raising high-priority security alert. Reason: {Reason}", reason);

            var subject = "🚨 High Priority Security Alert - Sportive ERP";
            var body = $@"
                <div style='font-family: Arial, sans-serif; direction: ltr; padding: 20px; border: 2px solid #ff4d4d; border-radius: 8px;'>
                    <h2 style='color: #ff4d4d;'>🚨 Security Risk Alert</h2>
                    <p><strong>Reason:</strong> {reason}</p>
                    <hr/>
                    <table style='width: 100%; border-collapse: collapse;'>
                        <tr style='background: #f8f9fa;'>
                            <td style='padding: 8px; font-weight: bold;'>IP Address:</td>
                            <td style='padding: 8px;'>{ipAddress}</td>
                        </tr>
                        <tr>
                            <td style='padding: 8px; font-weight: bold;'>User ID:</td>
                            <td style='padding: 8px;'>{userId ?? "Anonymous"}</td>
                        </tr>
                        <tr style='background: #f8f9fa;'>
                            <td style='padding: 8px; font-weight: bold;'>Correlation ID:</td>
                            <td style='padding: 8px; font-family: monospace;'>{correlationId}</td>
                        </tr>
                        <tr>
                            <td style='padding: 8px; font-weight: bold;'>Time:</td>
                            <td style='padding: 8px;'>{TimeHelper.GetEgyptTime():yyyy-MM-dd HH:mm:ss} (Egypt Time)</td>
                        </tr>
                    </table>
                    <br/>
                    <p style='color: #888; font-size: 11px;'>This alert was automatically generated by the Sportive Security Events Engine. Notifications for this endpoint/subject are throttled to 1 per hour per target.</p>
                </div>";

            try
            {
                await _email.SendEmailAsync(adminEmail, subject, body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SecurityEngine] Failed to send security alert email to {Admin}", adminEmail);
            }
        }
    }
}
