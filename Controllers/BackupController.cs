using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class BackupController : ControllerBase
{
    private readonly IBackupService _backup;

    public BackupController(IBackupService backup) => _backup = backup;

    // ── POST /api/backup/run ──────────────────────────
    // يشغّل backup الآن يدوياً
    [HttpPost("run")]
    public async Task<IActionResult> RunNow()
    {
        var result = await _backup.RunBackupAsync();
        return result.Success
            ? Ok(new {
                success  = true,
                fileName = result.FileName,
                sizeMb   = Math.Round((double)result.FileSizeBytes / 1024 / 1024, 2),
                durationS= Math.Round(result.Duration.TotalSeconds, 1),
                message  = "تم عمل النسخة الاحتياطية بنجاح ✅"
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
        var records = await _backup.GetHistoryAsync(1000);
        var record  = records.FirstOrDefault(r => r.Id == id);

        if (record == null) return NotFound(new { message = "النسخة غير موجودة" });
        if (string.IsNullOrEmpty(record.FilePath))
            return NotFound(new { message = "مسار الملف غير محدد" });
        if (!System.IO.File.Exists(record.FilePath))
            return NotFound(new { message = "الملف حُذف من الخادم" });

        var bytes = await System.IO.File.ReadAllBytesAsync(record.FilePath);
        return File(bytes, "application/gzip", record.FileName);
    }

    // ── DELETE /api/backup/cleanup ────────────────────
    [HttpDelete("cleanup")]
    public async Task<IActionResult> Cleanup([FromQuery] int keepDays = 30)
    {
        await _backup.DeleteOldBackupsAsync(keepDays);
        return Ok(new { message = $"تم حذف النسخ الأقدم من {keepDays} يوم" });
    }
}
