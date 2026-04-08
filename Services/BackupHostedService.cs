using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

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
        // لو الـ backup مش enabled، مش تشتغل خالص
        if (!_config.GetValue<bool>("Backup:Enabled", true))
        {
            _log.LogInformation("[Backup] Disabled by config");
            return;
        }

        _log.LogInformation("[Backup] Hosted service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = await CalculateDelayAsync(stoppingToken);
                _log.LogInformation("[Backup] Next backup in {h:N1}h ({time})",
                    delay.TotalHours, DateTime.UtcNow.Add(delay).ToString("HH:mm UTC"));

                await Task.Delay(delay, stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;

                await RunBackupSafeAsync(stoppingToken);
            }
            catch (TaskCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "[Backup] Hosted service error");
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
    }

    private async Task RunBackupSafeAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IBackupService>();
        var result = await svc.RunBackupAsync(ct);
        if (result.Success)
            _log.LogInformation("[Backup] Auto backup succeeded: {file} ({size}KB)",
                result.FileName, result.FileSizeBytes / 1024);
        else
            _log.LogError("[Backup] Auto backup failed: {error}", result.Error);

        // حذف النسخ القديمة
        var keepDays = _config.GetValue<int>("Backup:KeepDays", 30);
        await svc.DeleteOldBackupsAsync(keepDays);
    }

    private async Task<TimeSpan> CalculateDelayAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        // Get store settings
        var settings = await db.StoreInfo.OrderBy(s => s.StoreConfigId).FirstOrDefaultAsync(ct) ?? new StoreInfo();
        
        var targetTime = settings.BackupTime ?? "02:00";
        var parts      = targetTime.Split(':');
        var localHour  = int.Parse(parts[0]);
        var localMin   = parts.Length > 1 ? int.Parse(parts[1]) : 0;
        var offsetHours= settings.BackupUtcOffset;

        // Calculate UTC time: UTC = Local - Offset
        var nowUtcS    = DateTime.UtcNow;
        var nowLocal   = nowUtcS.AddHours(offsetHours);
        
        // Target local time today
        var nextLocal  = nowLocal.Date.AddHours(localHour).AddMinutes(localMin);
        
        // If local time has passed, move to tomorrow
        if (nextLocal <= nowLocal) nextLocal = nextLocal.AddDays(1);

        // Convert back to UTC for the delay
        var nextUtc    = nextLocal.AddHours(-offsetHours);
        
        return nextUtc - nowUtcS;
    }
}
