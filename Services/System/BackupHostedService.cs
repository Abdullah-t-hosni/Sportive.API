using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services;

public class BackupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory  _scopeFactory;
    private readonly IConfiguration        _config;
    private readonly ILogger<BackupHostedService> _log;

    public BackupHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<BackupHostedService> log)
    {
        _scopeFactory = scopeFactory;
        _config       = config;
        _log          = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue<bool>("Backup:Enabled", true))
        {
            _log.LogInformation("[Backup] Disabled by config");
            return;
        }

        _log.LogInformation("[Backup] Hosted service started. Periodic checker running every minute.");

        // Small initial delay on startup to let DB migrations / startup tasks finish cleanly
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndRunScheduledBackupAsync(stoppingToken);
            }
            catch (TaskCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "[Backup] Error during scheduled backup check");
            }

            // Check every 1 minute
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task CheckAndRunScheduledBackupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Get store settings
        var settings = await db.StoreInfo.OrderBy(s => s.StoreConfigId).FirstOrDefaultAsync(ct) ?? new StoreInfo();
        
        var targetTime = settings.BackupTime ?? "02:00";
        var parts      = targetTime.Split(':');
        var targetHour = int.TryParse(parts[0], out var h) ? h : 2;
        var targetMin  = parts.Length > 1 && int.TryParse(parts[1], out var m) ? m : 0;

        // Use store local time
        var nowLocal = TimeHelper.GetEgyptTime();

        // Check if current local time is at or past the scheduled target time today
        bool isScheduledTimeOrPast = (nowLocal.Hour > targetHour) || 
                                     (nowLocal.Hour == targetHour && nowLocal.Minute >= targetMin);

        if (!isScheduledTimeOrPast)
        {
            // Not yet time today
            return;
        }

        // Check if a backup has already run today (since midnight local time)
        var todayLocalStartUtc = TimeZoneInfo.ConvertTimeToUtc(nowLocal.Date, TimeHelper.GetStoreTimeZone());

        var backupDoneToday = await db.BackupRecords.AnyAsync(r => 
            r.CreatedAt >= todayLocalStartUtc && r.Success, ct);

        if (backupDoneToday)
        {
            // Today's backup is already completed
            return;
        }

        _log.LogInformation("[Backup] Triggering auto scheduled backup for {Date:yyyy-MM-dd} (Scheduled at {Time})", nowLocal.Date, targetTime);
        await RunBackupSafeAsync(ct);
    }

    private async Task RunBackupSafeAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IBackupService>();
        var result = await svc.RunBackupAsync("Scheduled", ct);
        if (result.Success)
            _log.LogInformation("[Backup] Auto backup succeeded: {file} ({size}KB)",
                result.FileName, result.FileSizeBytes / 1024);
        else
            _log.LogError("[Backup] Auto backup failed: {error}", result.Error);

        // Clean old backups
        var keepDays = _config.GetValue<int>("Backup:KeepDays", 30);
        await svc.DeleteOldBackupsAsync(keepDays);
    }
}
