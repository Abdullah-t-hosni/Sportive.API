using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
                var delay = CalculateDelay();
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

    // ── يحسب الوقت للـ backup القادم ─────────────────
    private TimeSpan CalculateDelay()
    {
        // وقت الـ backup من الـ config (افتراضي 2:00 صباحاً)
        var timeStr   = _config["Backup:DailyTime"] ?? "02:00";
        var parts     = timeStr.Split(':');
        var targetHour= int.Parse(parts[0]);
        var targetMin = parts.Length > 1 ? int.Parse(parts[1]) : 0;

        var now  = DateTime.UtcNow;
        var next = now.Date.AddHours(targetHour).AddMinutes(targetMin);

        // لو الوقت فات اليوم، خد بكره
        if (next <= now) next = next.AddDays(1);

        return next - now;
    }
}
