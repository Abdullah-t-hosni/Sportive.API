using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Services;

// ══════════════════════════════════════════════════════
// Interface
// ══════════════════════════════════════════════════════
public interface IBackupService
{
    Task<BackupResult> RunBackupAsync(CancellationToken ct = default);
    Task<List<BackupRecord>> GetHistoryAsync(int limit = 30);
    Task DeleteOldBackupsAsync(int keepDays = 30);
    Task<BackupResult> RestoreAsync(Stream fileStream, string fileName, CancellationToken ct = default);
}

public record BackupResult(
    bool     Success,
    string   FileName,
    long     FileSizeBytes,
    TimeSpan Duration,
    string?  Error = null
);

// ══════════════════════════════════════════════════════
// Implementation
// ══════════════════════════════════════════════════════
public class BackupService : IBackupService
{
    private readonly IConfiguration         _config;
    private readonly AppDbContext           _db;
    private readonly ILogger<BackupService> _log;

    // مجلد حفظ النسخ محلياً
    private string BackupDir => _config["Backup:LocalPath"] ?? "/tmp/sportive-backups";

    public BackupService(IConfiguration config, AppDbContext db, ILogger<BackupService> log)
    {
        _config = config;
        _db     = db;
        _log    = log;
        Directory.CreateDirectory(BackupDir);
    }

    // ══════════════════════════════════════════════════
    // Run Backup
    // ══════════════════════════════════════════════════
    public async Task<BackupResult> RunBackupAsync(CancellationToken ct = default)
    {
        var sw       = Stopwatch.StartNew();
        var stamp    = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var sqlFile  = Path.Combine(BackupDir, $"sportive_{stamp}.sql");
        var zipFile  = Path.Combine(BackupDir, $"sportive_{stamp}.sql.gz");

        try
        {
            _log.LogInformation("[Backup] Starting backup {stamp}", stamp);

            // ── 1. mysqldump ──────────────────────────
            await DumpDatabaseAsync(sqlFile, ct);

            // ── 2. Compress ───────────────────────────
            await CompressFileAsync(sqlFile, zipFile);
            File.Delete(sqlFile); // احذف الـ SQL الخام

            var fileSize = new FileInfo(zipFile).Length;
            _log.LogInformation("[Backup] Dump complete: {size} KB", fileSize / 1024);

            // ── 3. Send Email ─────────────────────────
            string? emailError = null;
            var emailEnabled = _config.GetValue<bool>("Backup:Email:Enabled");
            if (emailEnabled)
            {
                try   { await SendEmailAsync(zipFile, stamp, fileSize, ct); }
                catch (Exception ex) { emailError = ex.Message; _log.LogWarning("[Backup] Email failed: {e}", ex.Message); }
            }

            // ── 4. Save to DB log ─────────────────────
            var record = new BackupRecord
            {
                FileName     = Path.GetFileName(zipFile),
                FilePath     = zipFile,
                FileSizeBytes= fileSize,
                DurationMs   = sw.ElapsedMilliseconds,
                EmailSent    = emailEnabled && emailError == null,
                EmailError   = emailError,
                CreatedAt    = DateTime.UtcNow,
            };
            _db.BackupRecords.Add(record);
            await _db.SaveChangesAsync(ct);

            sw.Stop();
            _log.LogInformation("[Backup] Done in {ms}ms", sw.ElapsedMilliseconds);

            return new BackupResult(true, record.FileName, fileSize, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "[Backup] Failed");

            // سجّل الفشل
            _db.BackupRecords.Add(new BackupRecord
            {
                FileName     = $"FAILED_{stamp}",
                DurationMs   = sw.ElapsedMilliseconds,
                Success      = false,
                Error        = ex.Message,
                CreatedAt    = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync(CancellationToken.None);

            return new BackupResult(false, string.Empty, 0, sw.Elapsed, ex.Message);
        }
    }

    // ══════════════════════════════════════════════════
    // mysqldump
    // ══════════════════════════════════════════════════
    private async Task DumpDatabaseAsync(string outputPath, CancellationToken ct)
    {
        // فك الـ connection string
        var connStr = _config.GetConnectionString("DefaultConnection")
                   ?? _config["DATABASE_URL"]
                   ?? throw new InvalidOperationException("No connection string");

        var (host, port, db, user, password) = ParseConnectionString(connStr);

        var args = $"--host={host} --port={port} --user={user} " +
                   $"--password={password} --single-transaction " +
                   $"--routines --triggers --no-tablespaces {db}";

        var psi = new ProcessStartInfo
        {
            FileName               = "mysqldump",
            Arguments              = args,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("mysqldump process failed to start");

        await using var fileStream = File.Create(outputPath);
        var copyTask  = process.StandardOutput.BaseStream.CopyToAsync(fileStream, ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);

        await Task.WhenAll(copyTask, process.WaitForExitAsync(ct));
        var errorOutput = await errorTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"mysqldump exited {process.ExitCode}: {errorOutput}");

        _log.LogInformation("[Backup] mysqldump completed, output: {path}", outputPath);
    }

    // ══════════════════════════════════════════════════
    // Compress to .gz
    // ══════════════════════════════════════════════════
    private static async Task CompressFileAsync(string source, string dest)
    {
        await using var input  = File.OpenRead(source);
        await using var output = File.Create(dest);
        await using var gz     = new GZipStream(output, CompressionLevel.Optimal);
        await input.CopyToAsync(gz);
    }

    // ══════════════════════════════════════════════════
    // Send Email via SMTP
    // ══════════════════════════════════════════════════
    private async Task SendEmailAsync(string zipFile, string stamp, long fileSize, CancellationToken ct)
    {
        var cfg      = _config.GetSection("Backup:Email");
        var smtp     = cfg["SmtpHost"]   ?? throw new InvalidOperationException("SmtpHost missing");
        var port     = cfg.GetValue<int>("SmtpPort", 587);
        var user     = cfg["SmtpUser"]   ?? throw new InvalidOperationException("SmtpUser missing");
        var pass     = cfg["SmtpPass"]   ?? throw new InvalidOperationException("SmtpPass missing");
        var from     = cfg["From"]       ?? user;
        var toList   = cfg["To"]?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    ?? throw new InvalidOperationException("Email To missing");

        using var client = new SmtpClient(smtp, port)
        {
            UseDefaultCredentials = false, // Critical for Gmail
            Credentials      = new NetworkCredential(user, pass),
            EnableSsl        = cfg.GetValue<bool>("Ssl", true),
            DeliveryMethod   = SmtpDeliveryMethod.Network,
            Timeout          = 100_000, // Increase for large attachments
        };

        var dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC";
        var sizeStr = fileSize < 1024 * 1024
            ? $"{fileSize / 1024:N0} KB"
            : $"{fileSize / 1024.0 / 1024.0:N2} MB";

        using var msg = new MailMessage
        {
            From    = new MailAddress(from, "Sportive Backup"),
            Subject = $"✅ Sportive DB Backup — {stamp}",
            IsBodyHtml = true,
            Body = $"""
            <div style="font-family:Arial,sans-serif;direction:rtl;padding:20px">
              <h2 style="color:#0f3460">🗄️ نسخة احتياطية ناجحة</h2>
              <table style="border-collapse:collapse;width:100%">
                <tr><td style="padding:8px;color:#555">التاريخ</td><td style="padding:8px;font-weight:bold">{dateStr}</td></tr>
                <tr style="background:#f5f5f5"><td style="padding:8px;color:#555">اسم الملف</td><td style="padding:8px;font-family:monospace">{Path.GetFileName(zipFile)}</td></tr>
                <tr><td style="padding:8px;color:#555">حجم النسخة</td><td style="padding:8px;font-weight:bold">{sizeStr}</td></tr>
              </table>
              <p style="color:#888;font-size:12px;margin-top:20px">
                تم الإرسال تلقائياً من نظام Sportive<br/>
                الملف مضغوط بصيغة .sql.gz — يُفتح بأي برنامج ضغط
              </p>
            </div>
            """,
        };

        // أضف المستلمين
        foreach (var to in toList) msg.To.Add(to.Trim());

        // أرفق ملف النسخة (لو صغير — أقل من 25MB)
        var attachLimit = cfg.GetValue<long>("AttachLimitMb", 25) * 1024 * 1024;
        if (fileSize <= attachLimit)
        {
            msg.Attachments.Add(new Attachment(zipFile,
                "application/gzip") { Name = Path.GetFileName(zipFile) });
        }
        else
        {
            msg.Body += $"<p style='color:orange'>⚠️ الملف أكبر من الحد ({sizeStr}) — لم يُرفق</p>";
        }

        await client.SendMailAsync(msg, ct);
        _log.LogInformation("[Backup] Email sent to {recipients}", string.Join(", ", toList));
    }

    // ══════════════════════════════════════════════════
    // History + Cleanup
    // ══════════════════════════════════════════════════
    public async Task<List<BackupRecord>> GetHistoryAsync(int limit = 30)
        => await _db.BackupRecords
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync();

    public async Task DeleteOldBackupsAsync(int keepDays = 30)
    {
        var cutoff = DateTime.UtcNow.AddDays(-keepDays);
        var old = await _db.BackupRecords
            .Where(r => r.CreatedAt < cutoff)
            .ToListAsync();

        foreach (var r in old)
        {
            if (!string.IsNullOrEmpty(r.FilePath) && File.Exists(r.FilePath))
                File.Delete(r.FilePath);
        }

        _db.BackupRecords.RemoveRange(old);
        await _db.SaveChangesAsync();
        _log.LogInformation("[Backup] Cleaned {count} old backups", old.Count);
    }

    // ══════════════════════════════════════════════════
    // Restore Database
    // ══════════════════════════════════════════════════
    public async Task<BackupResult> RestoreAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var tempFile = Path.Combine(BackupDir, $"restore_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{fileName}");
        var finalSql = tempFile;

        try
        {
            _log.LogInformation("[Restore] Starting restoration from {file}", fileName);

            // 1. Save uploaded file to temp
            await using (var fs = File.Create(tempFile))
            {
                await fileStream.CopyToAsync(fs, ct);
            }

            // 2. If compressed, decompress
            if (fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                finalSql = tempFile.Replace(".gz", "");
                await DecompressFileAsync(tempFile, finalSql);
            }

            // 3. Run mysql command
            await ExecuteRestoreAsync(finalSql, ct);

            sw.Stop();
            _log.LogInformation("[Restore] Success in {ms}ms", sw.ElapsedMilliseconds);

            return new BackupResult(true, fileName, new FileInfo(tempFile).Length, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "[Restore] Failed");
            return new BackupResult(false, fileName, 0, sw.Elapsed, ex.Message);
        }
        finally
        {
            // Cleanup temp files
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (finalSql != tempFile && File.Exists(finalSql)) File.Delete(finalSql);
        }
    }

    private static async Task DecompressFileAsync(string source, string dest)
    {
        await using var input = File.OpenRead(source);
        await using var output = File.Create(dest);
        await using var gz = new GZipStream(input, CompressionMode.Decompress);
        await gz.CopyToAsync(output);
    }

    private async Task ExecuteRestoreAsync(string sqlPath, CancellationToken ct)
    {
        var connStr = _config.GetConnectionString("DefaultConnection")
                   ?? _config["DATABASE_URL"]
                   ?? throw new InvalidOperationException("No connection string");

        var (host, port, db, user, password) = ParseConnectionString(connStr);

        // Note: We use mysql command instead of mysqldump
        // Passing password via command line can be risky but is common in this setup
        var args = $"--host={host} --port={port} --user={user} --password={password} {db}";

        var psi = new ProcessStartInfo
        {
            FileName = "mysql",
            Arguments = args,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("mysql process failed to start");

        // ── IMPORTANT: StandardInput must be closed to signal EOF to mysql ──
        try
        {
            await using (var sqlStream = File.OpenRead(sqlPath))
            {
                await sqlStream.CopyToAsync(process.StandardInput.BaseStream, ct);
            }
            await process.StandardInput.BaseStream.FlushAsync(ct);
            process.StandardInput.Close(); // Signal EOF
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error feeding SQL to mysql process");
            if (!process.HasExited) process.Kill();
            throw;
        }

        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var errorOutput = await errorTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"mysql restore exited {process.ExitCode}: {errorOutput}");
    }

    // ══════════════════════════════════════════════════
    // Parse MySQL connection string
    // ══════════════════════════════════════════════════
    private static (string host, int port, string db, string user, string pass)
        ParseConnectionString(string connStr)
    {
        string host = "localhost", dbName = "", user = "", pass = "";
        int port = 3306;

        // Format: Server=x;Port=x;Database=x;User=x;Password=x
        foreach (var part in connStr.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length < 2) continue;
            var key = kv[0].Trim().ToLower();
            var val = kv[1].Trim();
            switch (key)
            {
                case "server": case "host":     host   = val; break;
                case "port":                    port   = int.TryParse(val, out var p) ? p : 3306; break;
                case "database":                dbName = val; break;
                case "user": case "uid": case "user id": user = val; break;
                case "password": case "pwd":    pass   = val; break;
            }
        }

        if (string.IsNullOrEmpty(dbName) || string.IsNullOrEmpty(user))
            throw new InvalidOperationException($"Cannot parse connection string: {connStr[..Math.Min(50, connStr.Length)]}...");

        return (host, port, dbName, user, pass);
    }
}
