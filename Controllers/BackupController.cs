using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class BackupController : ControllerBase
{
    private readonly IBackupService _backup;
    private readonly UserManager<AppUser> _userManager;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BackupController> _logger;
    private readonly IAuditService _audit;
    private readonly ITranslator _t;

    public BackupController(IBackupService backup, UserManager<AppUser> userManager, IMemoryCache cache, ILogger<BackupController> logger, IAuditService audit, ITranslator t)
    {
        _backup = backup;
        _userManager = userManager;
        _cache = cache;
        _logger = logger;
        _audit = audit;
        _t = t;
    }

    // ── POST /api/backup/run ──────────────────────────
    // يشغّل backup الآن يدوياً
    [HttpPost("run")]
    public async Task<IActionResult> RunNow()
    {
        _logger.LogInformation("Manual backup started by {User}", User.Identity?.Name);
        var result = await _backup.RunBackupAsync("Manual");
        
        return result.Success
            ? Ok(new {
                success  = true,
                id       = result.Id, 
                fileName = result.FileName,
                sizeMb   = Math.Round((double)result.FileSizeBytes / 1024 / 1024, 2),
                durationS= Math.Round(result.Duration.TotalSeconds, 1),
                message  = _t.Get("Backup.Success")
              })
            : StatusCode(500, new { success = false, error = result.Error });
    }

    // ── GET /api/backup/history ───────────────────────
    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] int limit = 30)
    {
        var records = await _backup.GetHistoryAsync(limit);
        return Ok(records.Select(r => new {
            r.Id, r.FileName, r.FileSizeBytes,
            sizeMb      = Math.Round((double)r.FileSizeBytes / 1024 / 1024, 2),
            r.DurationMs, r.Success,
            r.EmailSent, r.EmailError, r.Error,
            r.TriggerType, r.CreatedAt,
        }));
    }

    // ── GET /api/backup/download/{id} ─────────────────
    // تحميل ملف النسخة مباشرة (لو موجود محلياً)
    [HttpGet("download/{id}")]
    public async Task<IActionResult> Download(int id)
    {
        var record = await _backup.GetByIdAsync(id);

        if (record == null) return NotFound(new { message = _t.Get("Backup.NotFound") });
        if (string.IsNullOrEmpty(record.FilePath))
            return NotFound(new { message = _t.Get("Backup.FilePathNotSet") });
        if (!System.IO.File.Exists(record.FilePath))
            return NotFound(new { message = _t.Get("Backup.FileDeleted") });

        // 🛡️ Safe streaming for large files
        var stream = new FileStream(record.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        return File(stream, "application/gzip", record.FileName);
    }

    [HttpPost("restore/request-otp")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> RequestRestoreOtp([FromServices] IEmailService email)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var otp = new Random().Next(100000, 999999).ToString();
        var cacheKey = $"RestoreOtp_{user.Id}";
        
        _cache.Set(cacheKey, otp, TimeSpan.FromMinutes(5));

        _logger.LogCritical("RESTORE DATABASE OTP for {Email}: {OTP}", user.Email, otp);
        await email.SendEmailAsync(user.Email, _t.Get("Backup.RestoreOtpSubject"), _t.Get("Backup.RestoreOtpBody", otp));

        return Ok(new { success = true, message = _t.Get("Backup.OtpSent") });
    }

    public class RestoreRequest
    {
        public string Password { get; set; } = string.Empty;
        public string Otp { get; set; } = string.Empty;
        public IFormFile? File { get; set; }
    }

    [HttpPost("restore")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> Restore([FromForm] RestoreRequest req)
    {
        if (req.File == null || req.File.Length == 0)
            return BadRequest(new { message = _t.Get("Backup.InvalidFile") });

        if (req.File.Length > 500 * 1024 * 1024) // 500MB Limit
            return BadRequest(new { message = _t.Get("Backup.FileSizeLimit") });

        if (!req.File.FileName.EndsWith(".sql") && !req.File.FileName.EndsWith(".gz"))
            return BadRequest(new { message = _t.Get("Backup.AllowedExtensions") });

        // 🔐 3-Layer Security Check
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        if (!await _userManager.CheckPasswordAsync(user, req.Password))
            return BadRequest(new { success = false, message = _t.Get("Backup.InvalidPassword") });

        var cacheKey = $"RestoreOtp_{user.Id}";
        if (!_cache.TryGetValue(cacheKey, out string? cachedOtp) || cachedOtp != req.Otp)
            return BadRequest(new { success = false, message = _t.Get("Backup.InvalidOtp") });

        _cache.Remove(cacheKey);
        _logger.LogWarning("DATABASE RESTORE INITIATED by {User} using file {File}", User.Identity?.Name, req.File.FileName);
        
        await _audit.LogAsync("DatabaseRestore", "System", "DB", $"Restore initiated using {req.File.FileName}", user.Id, user.UserName);

        using var stream = req.File.OpenReadStream();
        var result = await _backup.RestoreAsync(stream, req.File.FileName, User.Identity?.Name);

        return result.Success
            ? Ok(new {
                success = true,
                message = _t.Get("Backup.RestoreSuccess"),
                durationS = Math.Round(result.Duration.TotalSeconds, 1)
              })
            : StatusCode(500, new { success = false, error = result.Error });
    }
}
